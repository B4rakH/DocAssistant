using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;

namespace DocAssistant.Gateway.Repositories
{
    public class ChatDocumentRepository(AppDbContext context) : IChatDocumentRepository
    {
        public async Task CreateAsync(ChatDocument chatDocument, bool isTransaction = false)
        {
            await context.ChatDocuments.AddAsync(chatDocument);

            if (!isTransaction)
                await context.SaveChangesAsync();            
        }
    }
}
