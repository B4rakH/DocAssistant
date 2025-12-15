using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;

namespace DocAssistant.Gateway.Services
{
    public interface IChatService
    {
        Task<Chat> CreateChatAsync(CreateChatRequest request);

        Task DeleteChatAsync(Guid chatId);
    }
}
