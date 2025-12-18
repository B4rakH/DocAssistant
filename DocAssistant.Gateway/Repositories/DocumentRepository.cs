

using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Document;
using DocAssistant.Gateway.Mappers;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Repositories
{
    public class DocumentRepository(AppDbContext context) : IDocumentRepository
    {
        public async Task<Document> CreateAsync(CreateDocumentDto documentDto, bool isTransaction = false)
        {
            var newDocument = documentDto.File.ToModelFromFile(documentDto.ChatId, documentDto.FilePath, DocumentStatus.Loading);

            await context.Documents.AddAsync(newDocument);

            if (!isTransaction)
                await context.SaveChangesAsync();

            return newDocument;
        }

        public async Task<bool> UpdateStatusAsync(
                Guid documentId,
                DocumentStatus newStatus,
                string? failureReason = null,
                CancellationToken cancellationToken = default)
        {
            var doc = await context.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

            // return false if it's null or already processed
            if (doc == null || doc.Status != DocumentStatus.Loading)
                return false;

            doc.Status = newStatus;
            //doc.UpdatedAt = DateTime.UtcNow; // Assuming you have this field

            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                doc.FailureReason = failureReason;
            }

            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
