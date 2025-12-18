using DocAssistant.Gateway.Data.Models;

namespace DocAssistant.Gateway.Services
{
    public interface IChatService
    {
        Task<List<Chat>> GetAllAsync();

        Task<Chat> CreateChatAsync(string chatName);

        Task<Chat?> GetByIdAsync(Guid chatId);
        
        Task UploadFilesAsync(Guid chatId, List<IFormFile> files);
        
        Task DeleteChatAsync(Guid chatId);
    }
}
