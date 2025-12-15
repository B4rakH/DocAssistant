using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Dtos.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Services
{
    public class DocumentResultConsumer(AppDbContext dbContext, ILogger<DocumentResultConsumer> logger) : IConsumer<DocumentProcessedEvent>
    {
        public async Task Consume(ConsumeContext<DocumentProcessedEvent> context)
        {
            try
            {
                // Find document by ID
                var doc = await dbContext.Documents.FirstOrDefaultAsync(d => d.Id == context.Message.DocumentId, context.CancellationToken);
                
                if (doc == null)
                {
                    logger.LogWarning("Document not found for result: {DocumentId}", context.Message.DocumentId);
                    return;
                }

                Console.WriteLine($"DocumentId: {context.Message.DocumentId} Success:{context.Message.Success}");

                // Update document status based on success
                doc.Status = context.Message.Success ? DocumentStatus.Completed : DocumentStatus.Failed;
                
                // Save error message if failed
                if (!context.Message.Success && !string.IsNullOrWhiteSpace(context.Message.ErrorMessage))
                {
                    doc.FailureReason = context.Message.ErrorMessage;
                }

                // Save changes to database
                await dbContext.SaveChangesAsync(context.CancellationToken);

                logger.LogInformation("Document {DocumentId} updated to {Status}", doc.Id, doc.Status);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating document status for {DocumentId}", context.Message.DocumentId);
                throw; // Re-throw to let MassTransit handle retry
            }
        }
    }
}
