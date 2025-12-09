using DocAssistant.Gateway.Dtos.Events;

namespace DocAssistant.Gateway.Services
{
    public interface IMessageProducer
    {
        Task<bool> PublishDocumentUploadedAsync(DocumentUploadedEvent message);
    }
}
