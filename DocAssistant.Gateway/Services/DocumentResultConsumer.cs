using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Dtos.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Services
{
    public class DocumentResultConsumer : IConsumer<DocumentProcessedEvent>
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DocumentResultConsumer> _logger;

        public DocumentResultConsumer(AppDbContext context, ILogger<DocumentResultConsumer> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<DocumentProcessedEvent> context)
        {
            try
            {
                // Find document by ID
                var doc = await _context.Documents.FirstOrDefaultAsync(d => d.Id == context.Message.DocumentId, context.CancellationToken);
                
                if (doc == null)
                {
                    _logger.LogWarning("Document not found for result: {DocumentId}", context.Message.DocumentId);
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
                await _context.SaveChangesAsync(context.CancellationToken);

                _logger.LogInformation("Document {DocumentId} updated to {Status}", doc.Id, doc.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating document status for {DocumentId}", context.Message.DocumentId);
                throw; // Re-throw to let MassTransit handle retry
            }
        }
    }
}
