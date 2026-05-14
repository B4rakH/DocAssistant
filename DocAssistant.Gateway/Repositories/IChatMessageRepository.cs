using DocAssistant.Gateway.Data.Models;

namespace DocAssistant.Gateway.Repositories
{
    public interface IChatMessageRepository
    {
        Task<List<ChatMessage>> GetAllAsync(Guid chatId);

        Task CreateAsync(ChatMessage chatMessage);
        
        Task DeleteAsync(Guid messageId);

        Task<bool> ExistsResponseForMessageAsync(Guid chatId, DateTime timestamp);
    }
}
