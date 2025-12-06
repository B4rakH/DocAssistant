using DocAssistant.Gateway.Dtos.Events;

namespace DocAssistant.Gateway.Services
{
    public interface IRabbitMQService
    {
        Task PublishDocumentUploadedAsync(DocumentUploadedEvent message);
    }
}
