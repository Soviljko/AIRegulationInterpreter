using Common.Models;
using Microsoft.ServiceFabric.Services.Remoting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Common.Contracts
{
    public interface ILLMService : IService
    {
        Task<QueryResponse> GenerateAnswerAsync(string question, List<DocumentSection> sections);
    }
}
