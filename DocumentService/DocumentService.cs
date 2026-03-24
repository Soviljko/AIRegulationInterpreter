using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Common.Contracts;
using Common.Models;
using Microsoft.ServiceFabric.Data.Collections;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;

namespace DocumentService
{
    internal sealed class DocumentService : StatefulService, IDocumentService
    {
        private const string DocumentDictionaryName = "documents";
        private const string AzuriteConnectionString = "UseDevelopmentStorage=true";
        private const string TableName = "Documents";

        public DocumentService(StatefulServiceContext context) : base(context) { }

        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return this.CreateServiceRemotingReplicaListeners();
        }

        public async Task<string> UploadDocumentAsync(DocumentDto document)
        {
            var documents = await StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(DocumentDictionaryName);

            string docId = $"DOC{DateTime.UtcNow:yyyyMMddHHmmss}";
            var sections = ParseSections(document.Content, docId);

            var version = new DocumentVersion
            {
                DocumentId = docId,
                Title = document.Title,
                DocumentType = document.DocumentType,
                VersionNumber = 1,
                ValidFrom = document.ValidFrom,
                ValidTo = document.ValidTo,
                CreatedAt = DateTime.UtcNow,
                Content = document.Content,
                Sections = sections
            };

            using (var tx = StateManager.CreateTransaction())
            {
                var existing = await documents.TryGetValueAsync(tx, docId);
                if (existing.HasValue)
                {
                    var existingVersion = JsonSerializer.Deserialize<DocumentVersion>(existing.Value);
                    version.VersionNumber = existingVersion!.VersionNumber + 1;
                }

                await documents.AddOrUpdateAsync(tx, docId,
                    JsonSerializer.Serialize(version),
                    (k, v) => JsonSerializer.Serialize(version));

                await tx.CommitAsync();
            }

            await PersistToTableAsync(version);

            ServiceEventSource.Current.ServiceMessage(Context, $"Document uploaded: {docId}, sections: {sections.Count}");
            return docId;
        }

        private static readonly HashSet<string> StopWords = new HashSet<string>
        {
            "da", "li", "na", "i", "se", "je", "u", "za", "od", "do", "ili",
            "kako", "koji", "koja", "koje", "koliko", "može", "moze", "biti",
            "bi", "su", "sa", "po", "što", "sto", "ako", "ali", "pri", "sve",
            "on", "ona", "ono", "mi", "vi", "oni", "taj", "ta", "to", "ovaj",
            "ova", "ovo", "ima", "te", "ne", "pa", "ce", "će", "mu", "ga",
            "ih", "im", "joj", "nam", "vam", "nje", "njoj", "njen", "njegov",
            "koje", "kojoj", "kojih", "kojima", "svaki", "svaka", "svako"
        };

        public async Task<List<DocumentSection>> GetRelevantSectionsAsync(string query, DateTime contextDate)
        {
            var documents = await StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(DocumentDictionaryName);
            var relevantSections = new List<(DocumentSection section, int score)>();

            var queryWords = query.ToLower()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 2 && !StopWords.Contains(w))
                .ToArray();

            if (queryWords.Length == 0) return new List<DocumentSection>();

            using (var tx = StateManager.CreateTransaction())
            {
                var enumerator = (await documents.CreateEnumerableAsync(tx)).GetAsyncEnumerator();
                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    var version = JsonSerializer.Deserialize<DocumentVersion>(enumerator.Current.Value);
                    if (version == null) continue;

                    if (version.ValidFrom > contextDate) continue;
                    if (version.ValidTo.HasValue && version.ValidTo.Value < contextDate) continue;

                    foreach (var section in version.Sections)
                    {
                        string contentLower = section.Content?.ToLower() ?? "";
                        string titleLower = section.Title?.ToLower() ?? "";

                        int score = 0;

                        foreach (var word in queryWords)
                        {
                            if (contentLower.Contains(word)) score += 1;
                            if (titleLower.Contains(word)) score += 3;
                        }

                        // Bonus za uzastopne reči (fraze)
                        for (int i = 0; i < queryWords.Length - 1; i++)
                        {
                            string phrase = queryWords[i] + " " + queryWords[i + 1];
                            if (contentLower.Contains(phrase)) score += 5;
                        }

                        if (score > 0)
                            relevantSections.Add((section, score));
                    }
                }
            }

            return relevantSections
                .OrderByDescending(x => x.score)
                .Take(15)
                .Select(x => x.section)
                .ToList();
        }

        public async Task<List<DocumentVersion>> GetVersionHistoryAsync(string documentId)
        {
            var tableClient = new TableClient(AzuriteConnectionString, TableName);
            var versions = new List<DocumentVersion>();

            try
            {
                await foreach (var entity in tableClient.QueryAsync<TableEntity>(
                    filter: $"PartitionKey eq '{documentId}'"))
                {
                    if (entity.TryGetValue("Data", out var data))
                    {
                        var version = JsonSerializer.Deserialize<DocumentVersion>(data.ToString()!);
                        if (version != null) versions.Add(version);
                    }
                }
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(Context, $"GetVersionHistory error: {ex.Message}");
            }

            return versions.OrderBy(v => v.VersionNumber).ToList();
        }

        public async Task<List<DocumentVersion>> GetAllDocumentsAsync()
        {
            var documents = await StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(DocumentDictionaryName);
            var result = new List<DocumentVersion>();

            using (var tx = StateManager.CreateTransaction())
            {
                var enumerator = (await documents.CreateEnumerableAsync(tx)).GetAsyncEnumerator();
                while (await enumerator.MoveNextAsync(CancellationToken.None))
                {
                    var version = JsonSerializer.Deserialize<DocumentVersion>(enumerator.Current.Value);
                    if (version != null) result.Add(version);
                }
            }

            return result;
        }

        private List<DocumentSection> ParseSections(string content, string docId)
        {
            var sections = new List<DocumentSection>();
            int orderIndex = 0;

            var clanPattern = new Regex(@"Član\s+(\d+)", RegexOptions.IgnoreCase);
            var stavPattern = new Regex(@"Stav\s+(\d+)", RegexOptions.IgnoreCase);

            var clanMatches = clanPattern.Matches(content);

            if (clanMatches.Count == 0)
            {
                sections.Add(new DocumentSection
                {
                    SectionId = $"{docId}-S1",
                    DocumentId = docId,
                    Title = "Sadržaj",
                    Content = content,
                    Type = SectionType.Clan,
                    OrderIndex = orderIndex++
                });
                return sections;
            }

            for (int i = 0; i < clanMatches.Count; i++)
            {
                var match = clanMatches[i];
                int clanNum = int.Parse(match.Groups[1].Value);
                int startPos = match.Index;
                int endPos = (i + 1 < clanMatches.Count) ? clanMatches[i + 1].Index : content.Length;

                string clanContent = content.Substring(startPos, endPos - startPos).Trim();
                string clanId = $"{docId}-CL{clanNum}";

                sections.Add(new DocumentSection
                {
                    SectionId = clanId,
                    DocumentId = docId,
                    Title = $"Član {clanNum}",
                    Content = clanContent,
                    Type = SectionType.Clan,
                    OrderIndex = orderIndex++
                });

                var stavMatches = stavPattern.Matches(clanContent);
                for (int j = 0; j < stavMatches.Count; j++)
                {
                    var stavMatch = stavMatches[j];
                    int stavNum = int.Parse(stavMatch.Groups[1].Value);
                    int stavStart = stavMatch.Index;
                    int stavEnd = (j + 1 < stavMatches.Count) ? stavMatches[j + 1].Index : clanContent.Length;

                    string stavContent = clanContent.Substring(stavStart, stavEnd - stavStart).Trim();

                    sections.Add(new DocumentSection
                    {
                        SectionId = $"{clanId}-ST{stavNum}",
                        DocumentId = docId,
                        Title = $"Član {clanNum}, Stav {stavNum}",
                        Content = stavContent,
                        Type = SectionType.Stav,
                        OrderIndex = orderIndex++
                    });
                }
            }

            return sections;
        }

        private async Task PersistToTableAsync(DocumentVersion version)
        {
            try
            {
                var tableClient = new TableClient(AzuriteConnectionString, TableName);
                await tableClient.CreateIfNotExistsAsync();

                var entity = new TableEntity(version.DocumentId, version.VersionNumber.ToString())
                {
                    { "Title", version.Title },
                    { "DocumentType", version.DocumentType },
                    { "ValidFrom", version.ValidFrom },
                    { "ValidTo", version.ValidTo?.ToString() ?? "" },
                    { "CreatedAt", version.CreatedAt },
                    { "Data", JsonSerializer.Serialize(version) }
                };

                await tableClient.UpsertEntityAsync(entity);
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.ServiceMessage(Context, $"Azure Table persist failed: {ex.Message}");
            }
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var documents = await StateManager.GetOrAddAsync<IReliableDictionary<string, string>>(DocumentDictionaryName);
                using (var tx = StateManager.CreateTransaction())
                {
                    var count = await documents.GetCountAsync(tx);
                    ServiceEventSource.Current.ServiceMessage(Context, $"DocumentService alive. Documents: {count}");
                }
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }
    }
}
