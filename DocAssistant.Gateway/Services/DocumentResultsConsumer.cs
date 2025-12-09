using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Services;

public class DocumentResultsConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMqService _rabbitMqService;
    private readonly ILogger<DocumentResultsConsumer> _logger;

    public DocumentResultsConsumer(IServiceProvider serviceProvider, RabbitMqService rabbitMqService, ILogger<DocumentResultsConsumer> logger)
    {
        _serviceProvider = serviceProvider;
        _rabbitMqService = rabbitMqService;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // register handler; called only when messages arrive
        return _rabbitMqService.StartConsumingResultsAsync(async result =>
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == result.DocumentId, stoppingToken);
                if (doc == null)
                {
                    _logger.LogWarning("Document not found for result: {DocumentId}", result.DocumentId);
                    return;
                }

                doc.Status = result.Success ? DocumentStatus.Completed : DocumentStatus.Failed;

                Console.WriteLine($"Complete status: {doc.Status}");

                await db.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Document {DocumentId} updated to {Status}", doc.Id, doc.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling processed document result {DocumentId}", result.DocumentId);
            }
        }, stoppingToken);
    }
}