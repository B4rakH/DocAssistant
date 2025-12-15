using DocAssistant.Gateway.Data.Models;

namespace DocAssistant.Gateway.Repositories
{
    public interface IDocumentRepository
    {
        Task<Document> CreateAsync(IFormFile file, string filePath, bool isTransaction = false);
    }
}
