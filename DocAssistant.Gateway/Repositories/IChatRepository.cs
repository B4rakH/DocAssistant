using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;

namespace DocAssistant.Gateway.Repositories
{
    public interface IChatRepository
    {
        Task<Chat> CreateAsync(CreateChatRequest chatRequest, bool isTransaction = false);
    }
}
