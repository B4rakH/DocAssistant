using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Dtos.Events;
using DocAssistant.Gateway.Hubs;
using DocAssistant.Gateway.Repositories;
using DocAssistant.Gateway.Services;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace DocAssistant.Gateway.Consumers
{
    public class DocumentResultConsumer(
        IDocumentRepository documentRepository,
        IMinIOService minioService,
        IHubContext<ChatHub> hubContext,
        ILogger<DocumentResultConsumer> logger) : IConsumer<DocumentProcessedEvent>
    {
        public async Task Consume(ConsumeContext<DocumentProcessedEvent> context)
        {
            var message = context.Message;

            try
            {
                var newStatus = message.Success ? DocumentStatus.Completed : DocumentStatus.Failed;

                logger.LogInformation(
                    "Received document processing result for document {DocumentId}. Success: {Success} [CorrelationId: {CorrelationId}]",
                    message.DocumentId,
                    message.Success,
                    message.CorrelationId);

                // Handle failed documents: delete from both database and MinIO
                if (!message.Success)
                {
                    await HandleFailedDocumentAsync(message.DocumentId, message.ErrorMessage, context.CancellationToken);
                    return;
                }

                // Load document before updating (need chatId for SignalR notification)
                var document = await documentRepository.GetByIdAsync(message.DocumentId, context.CancellationToken);

                // Update successful document status
                var updated = await documentRepository.UpdateStatusAsync(
                    message.DocumentId,
                    newStatus,
                    message.ErrorMessage,
                    context.CancellationToken);

                if (!updated)
                {
                    logger.LogWarning(
                        "Document not found or already processed: {DocumentId}",
                        message.DocumentId);
                    return;
                }

                logger.LogInformation("Document {DocumentId} updated to {Status}", message.DocumentId, newStatus);

                // Notify connected clients about document completion
                await NotifyDocumentStatusAsync(document?.ChatId, message.DocumentId, "Completed", document?.FileName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to process document result for document {DocumentId} [CorrelationId: {CorrelationId}]",
                    message.DocumentId,
                    message.CorrelationId);
                throw; // Re-throw for MassTransit retry
            }
        }

        private async Task HandleFailedDocumentAsync(
            Guid documentId,
            string? errorMessage,
            CancellationToken cancellationToken)
        {
            logger.LogWarning(
                "Document {DocumentId} processing failed: {Error}. Starting cleanup...",
                documentId,
                errorMessage ?? "Unknown error");

            try
            {
                // Step 1: Get document info (need FilePath for MinIO cleanup)
                var document = await documentRepository.GetByIdAsync(documentId, cancellationToken);

                if (document == null)
                {
                    logger.LogWarning(
                        "Document {DocumentId} not found in database. Skipping cleanup.",
                        documentId);
                    return;
                }

                var filePath = document.FilePath;

                // Step 2: Delete from PostgreSQL (critical operation)
                var deletedFromDb = await documentRepository.DeleteAsync(documentId, cancellationToken);

                if (!deletedFromDb)
                {
                    logger.LogError(
                        "Failed to delete document {DocumentId} from database",
                        documentId);
                    throw new InvalidOperationException($"Failed to delete document {documentId} from database");
                }

                logger.LogInformation(
                    "Successfully deleted failed document {DocumentId} from database",
                    documentId);

                // Step 3: Delete from MinIO
                if (!string.IsNullOrWhiteSpace(filePath))
                {
                    try
                    {
                        await minioService.DeleteFileAsync(filePath);
                        logger.LogInformation(
                            "Successfully deleted file from MinIO: {FilePath}",
                            filePath);
                    }
                    catch (Exception minioEx)
                    {
                        // Log warning but don't fail the operation
                        // File might already be deleted or MinIO might be temporarily unavailable
                        logger.LogWarning(minioEx,
                            "Failed to delete file from MinIO (non-critical): {FilePath}. Manual cleanup may be required.",
                            filePath);
                    }
                }

                logger.LogInformation(
                    "Cleanup completed for failed document {DocumentId}",
                    documentId);

                // Notify connected clients about document failure
                await NotifyDocumentStatusAsync(document.ChatId, documentId, "Failed", document.FileName, errorMessage);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Critical error during cleanup of failed document {DocumentId}",
                    documentId);
                throw; // Re-throw for MassTransit retry
            }
        }

        // Best-effort SignalR notification — never crashes the consumer
        private async Task NotifyDocumentStatusAsync(Guid? chatId, Guid documentId, string status, string? fileName, string? error = null)
        {
            if (chatId == null) return;

            try
            {
                await hubContext.Clients.Group(chatId.ToString()!)
                    .SendAsync("DocumentStatusChanged", new
                    {
                        DocumentId = documentId,
                        ChatId = chatId,
                        Status = status,
                        FileName = fileName,
                        Error = error
                    });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send SignalR notification for document {DocumentId}", documentId);
            }
        }
    }
}
