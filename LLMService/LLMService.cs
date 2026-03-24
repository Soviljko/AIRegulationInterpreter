using Common.Contracts;
using Common.Models;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LLMService
{
    internal sealed class LLMService : StatelessService, ILLMService
    {
        private const string ModelName = "stepfun/step-3.5-flash:free";
        private const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";

        public LLMService(StatelessServiceContext context) : base(context) { }

        protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
        {
            return this.CreateServiceRemotingInstanceListeners();
        }

        public async Task<QueryResponse> GenerateAnswerAsync(string question, List<DocumentSection> sections)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                return new QueryResponse
                {
                    Explanation = "API key is not configured. Set the OPENAI_API_KEY environment variable.",
                    HasSufficientInfo = false,
                    Confidence = 0,
                    Citations = new List<CitationDto>()
                };
            }

            if (sections == null || sections.Count == 0)
            {
                return new QueryResponse
                {
                    Explanation = "No relevant document sections were found to answer this question.",
                    HasSufficientInfo = false,
                    Confidence = 0,
                    Citations = new List<CitationDto>()
                };
            }

            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine("RELEVANT DOCUMENT SECTIONS:");
            contextBuilder.AppendLine();
            foreach (var section in sections)
            {
                contextBuilder.AppendLine($"[ID: {section.SectionId}] {section.Title}");
                contextBuilder.AppendLine(section.Content);
                contextBuilder.AppendLine("---");
            }

            var systemPrompt = @"You are an assistant for interpreting laws and regulations. Answer EXCLUSIVELY based on the provided context.
If the information is not in the context, explicitly state that.
Respond in the following JSON format with no additional comments:
{
  ""explanation"": ""Clear explanation in English"",
  ""citations"": [
    {""sectionId"": ""section_ID"", ""relevantText"": ""Exact quote from the document""}
  ],
  ""confidence"": 85
}
Confidence calibration rules:
- 90-100: The context directly and completely answers every aspect of the question with explicit text.
- 70-89: The context answers the main question but some details are missing or implied.
- 40-69: The context only partially covers the question or the answer requires some inference.
- 10-39: The context is loosely related but does not clearly answer the question.
- 0-9: The context contains no relevant information to answer the question.";

            var userMessage = $"Context:\n{contextBuilder}\n\nQuestion: {question}";

            try
            {
                var clientOptions = new OpenAI.OpenAIClientOptions
                {
                    Endpoint = new Uri(OpenRouterBaseUrl)
                };
                var chatClient = new ChatClient(ModelName, new ApiKeyCredential(apiKey), clientOptions);

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userMessage)
                };

                var completion = await chatClient.CompleteChatAsync(messages);
                var responseText = completion.Value.Content[0].Text;

                ServiceEventSource.Current.ServiceMessage(Context, $"LLM response received, length: {responseText.Length}");
                return ParseLLMResponse(responseText, sections);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(Context, $"LLM error: {ex.Message}");
                return new QueryResponse
                {
                    Explanation = $"Error communicating with AI service: {ex.Message}",
                    HasSufficientInfo = false,
                    Confidence = 0,
                    Citations = new List<CitationDto>()
                };
            }
        }

        private QueryResponse ParseLLMResponse(string responseText, List<DocumentSection> sections)
        {
            try
            {
                int jsonStart = responseText.IndexOf('{');
                int jsonEnd = responseText.LastIndexOf('}');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    string jsonPart = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    var parsed = JsonSerializer.Deserialize<LLMJsonResponse>(jsonPart, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (parsed != null)
                    {
                        var citations = new List<CitationDto>();
                        foreach (var c in parsed.Citations ?? new List<LLMCitation>())
                        {
                            var section = sections.FirstOrDefault(s => s.SectionId == c.SectionId);
                            citations.Add(new CitationDto
                            {
                                SectionId = c.SectionId,
                                DocumentTitle = section?.DocumentId ?? "",
                                SectionTitle = section?.Title ?? c.SectionId,
                                RelevantText = c.RelevantText
                            });
                        }

                        return new QueryResponse
                        {
                            Explanation = parsed.Explanation ?? responseText,
                            Citations = citations,
                            Confidence = parsed.Confidence,
                            HasSufficientInfo = parsed.Confidence > 30
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(Context, $"JSON parse error: {ex.Message}");
            }

            return new QueryResponse
            {
                Explanation = responseText,
                Citations = new List<CitationDto>(),
                Confidence = 50,
                HasSufficientInfo = true
            };
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ServiceEventSource.Current.ServiceMessage(Context, "LLMService alive.");
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }
    }

    internal class LLMJsonResponse
    {
        public string Explanation { get; set; } = "";
        public List<LLMCitation> Citations { get; set; } = new();
        public int Confidence { get; set; }
    }

    internal class LLMCitation
    {
        public string SectionId { get; set; } = "";
        public string RelevantText { get; set; } = "";
    }
}
