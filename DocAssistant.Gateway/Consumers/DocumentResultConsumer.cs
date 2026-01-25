using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Dtos.Events;
using DocAssistant.Gateway.Repositories;
using MassTransit;

namespace DocAssistant.Gateway.Consumers
{
    public class DocumentResultConsumer(IDocumentRepository documentRepository, ILogger<DocumentResultConsumer> logger) : IConsumer<DocumentProcessedEvent>
    {
        public async Task Consume(ConsumeContext<DocumentProcessedEvent> context)
        {
            var message = context.Message;

            try
            {

                var newStatus = message.Success ? DocumentStatus.Completed : DocumentStatus.Failed;

                // TODO: Opitimize management of the onFail case (Delete failed files in the disk)

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

                // TODO:Notify user via SignalR
                // 1. Inject IHubContext<NotificationHub> into this Consumer's constructor.
                // 2. Use 'message.UserId' (Ensure your event includes UserId) to target the specific user.
                // Example: 
                //    await _hubContext.Clients.User(message.UserId.ToString())
                //        .SendAsync("DocumentProcessed", new { 
                //             id = message.DocumentId, 
                //             status = newStatus, 
                //             error = message.ErrorMessage 
                //        });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating document status for {DocumentId}", context.Message.DocumentId);
                throw; // Re-throw to let MassTransit handle retry
            }
        }
    }
}
