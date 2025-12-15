

using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Mappers;

namespace DocAssistant.Gateway.Repositories
{
    public class DocumentRepository(AppDbContext context) : IDocumentRepository
    {
        public async Task<Document> CreateAsync(IFormFile file, string filePath, bool isTransaction = false)
        {
            var newDocument = file.ToModelFromFile(filePath, DocumentStatus.Pending);

            await context.Documents.AddAsync(newDocument);
            
            if (!isTransaction)
                await context.SaveChangesAsync();
            
            return newDocument;
        }
    }
}
