using DocAssistant.Gateway.Data.Models;

namespace DocAssistant.Gateway.Repositories
{
    public interface IChatDocumentRepository
    {
        Task CreateAsync(ChatDocument chatDocument, bool isTransaction = false);
    }
}
