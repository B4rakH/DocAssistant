using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Dtos.Events;
using DocAssistant.Gateway.Repositories;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Services
{
    public class DocumentResultConsumer(IDocumentRepository documentRepository, ILogger<DocumentResultConsumer> logger) : IConsumer<DocumentProcessedEvent>
    {
        public async Task Consume(ConsumeContext<DocumentProcessedEvent> context)
        {
            var message = context.Message;

            try
            {

                var newStatus = message.Success ? DocumentStatus.Completed : DocumentStatus.Failed;


                Console.WriteLine($"DocumentId: {message.DocumentId} Success:{message.Success}");

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

                logger.LogInformation("Document {DocumentId} updated to {newStatus}", message.DocumentId, newStatus);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating document status for {DocumentId}", context.Message.DocumentId);
                throw; // Re-throw to let MassTransit handle retry
            }
        }
    }
}
