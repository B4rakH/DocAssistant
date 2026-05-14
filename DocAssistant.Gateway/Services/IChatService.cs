using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.ChatMessage;

namespace DocAssistant.Gateway.Services
{
    public interface IChatService
    {
        Task<List<Chat>> GetAllAsync();

        Task<Chat> CreateChatAsync(string chatName);

        Task<Chat?> GetByIdAsync(Guid chatId);

        Task<ChatMessageResponse> PostMessageAsync(Guid chatId, ChatMessageRequest request);

        Task UploadFilesAsync(Guid chatId, List<IFormFile> files);
        
        Task DeleteChatAsync(Guid chatId);
    }
}
