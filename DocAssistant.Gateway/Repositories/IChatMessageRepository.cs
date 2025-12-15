using DocAssistant.Gateway.Data.Models;

namespace DocAssistant.Gateway.Repositories
{
    public interface IChatMessageRepository
    {
        Task <List<ChatMessage>> GetAllAsync(Guid chatId);

    }
}
