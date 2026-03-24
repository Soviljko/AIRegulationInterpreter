using Common.Models;
using Microsoft.ServiceFabric.Services.Remoting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Common.Contracts
{
    public interface IDocumentService : IService
    {
        Task<string> UploadDocumentAsync(DocumentDto document);
        Task<List<DocumentSection>> GetRelevantSectionsAsync(string query, DateTime contextDate);
        Task<List<DocumentVersion>> GetVersionHistoryAsync(string documentId);
        Task<List<DocumentVersion>> GetAllDocumentsAsync();
    }
}
