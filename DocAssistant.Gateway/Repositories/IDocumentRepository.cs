using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Document;

namespace DocAssistant.Gateway.Repositories
{
    public interface IDocumentRepository
    {
        Task<Document> CreateAsync(CreateDocumentDto documentDto, bool isTransaction = false);

        Task<bool> UpdateStatusAsync(
            Guid documentId,
            DocumentStatus newStatus,
            string? failureReason = null,
            CancellationToken cancellationToken = default);

    }
}
