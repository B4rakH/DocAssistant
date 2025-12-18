using DocAssistant.Gateway.Data.Models;


namespace DocAssistant.Gateway.Repositories
{
    public interface IChatRepository
    {

        Task<List<Chat>> GetAllAsync();
        Task<Chat?> GetByIdAsync(Guid chatId);
        Task<Chat> CreateAsync(string chatName, bool isTransaction = false);
        Task DeleteAsync(Guid chatId, bool isTransaction = false);
        Task<bool> ExistsByIdAsync(Guid chatId);
    }
}
