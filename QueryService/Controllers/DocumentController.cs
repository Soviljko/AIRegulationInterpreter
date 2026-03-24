using Common.Contracts;
using Common.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.ServiceFabric.Services.Client;
using Microsoft.ServiceFabric.Services.Remoting.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QueryService.Controllers
{
    [ApiController]
    [Route("api")]
    public class DocumentController : ControllerBase
    {
        private static readonly Uri DocumentServiceUri = new Uri("fabric:/AIRegulationInterpreter/DocumentService");
        private static readonly Uri LLMServiceUri = new Uri("fabric:/AIRegulationInterpreter/LLMService");

        [HttpPost("documents")]
        public async Task<IActionResult> UploadDocument([FromBody] DocumentDto document)
        {
            if (document == null || string.IsNullOrEmpty(document.Content))
                return BadRequest("Dokument ne smije biti prazan.");

            var docService = ServiceProxy.Create<IDocumentService>(DocumentServiceUri, new ServicePartitionKey(0));
            var docId = await docService.UploadDocumentAsync(document);
            return Ok(new { documentId = docId, message = "Dokument uspješno uploadovan." });
        }

        [HttpGet("documents")]
        public async Task<IActionResult> GetAllDocuments()
        {
            var docService = ServiceProxy.Create<IDocumentService>(DocumentServiceUri, new ServicePartitionKey(0));
            var documents = await docService.GetAllDocumentsAsync();
            return Ok(documents);
        }

        [HttpGet("documents/{id}/history")]
        public async Task<IActionResult> GetVersionHistory(string id)
        {
            var docService = ServiceProxy.Create<IDocumentService>(DocumentServiceUri, new ServicePartitionKey(0));
            var history = await docService.GetVersionHistoryAsync(id);
            return Ok(history);
        }

        [HttpPost("query")]
        public async Task<IActionResult> Query([FromBody] QueryRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Question))
                return BadRequest("Pitanje ne smije biti prazno.");

            var contextDate = request.ContextDate ?? DateTime.UtcNow;

            var docService = ServiceProxy.Create<IDocumentService>(DocumentServiceUri, new ServicePartitionKey(0));
            var sections = await docService.GetRelevantSectionsAsync(request.Question, contextDate);

            var llmService = ServiceProxy.Create<ILLMService>(LLMServiceUri);
            var response = await llmService.GenerateAnswerAsync(request.Question, sections);

            return Ok(response);
        }
    }
}
