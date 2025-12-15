using DocAssistant.Gateway.Data.Models;

namespace DocAssistant.Gateway.Services
{
    public interface IChatMessageService
    {
        Task<List<ChatMessage>> GetAllMesageAsync(Guid chatId);
    }
}
