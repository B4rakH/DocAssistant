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
            var newDocument = documentDto.File.ToModelFromFile(documentDto.ChatId, documentDto.FilePath, DocumentStatus.Pending);

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

            if (doc == null || (doc.Status != DocumentStatus.Pending && doc.Status != DocumentStatus.Processing))
                return false;

            doc.Status = newStatus;

            if (!string.IsNullOrWhiteSpace(failureReason))
            {
                doc.FailureReason = failureReason;
            }

            await context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> AllDocumentsCompletedAsync(Guid chatId)
        {
            return await context.Documents
                .Where(d => d.ChatId == chatId)
                .AllAsync(d => d.Status == DocumentStatus.Completed);
        }

        public async Task<Document?> GetByIdAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            return await context.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        }

        public async Task<bool> DeleteAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            var document = await context.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

            if (document == null)
                return false;

            context.Documents.Remove(document);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
    }
}
