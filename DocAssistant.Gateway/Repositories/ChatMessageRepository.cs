using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Repositories
{
    public class ChatMessageRepository(AppDbContext context) : IChatMessageRepository
    {
        public async Task<List<ChatMessage>> GetAllAsync(Guid chatId)
        {
            return await context.ChatMessages
                .Where(message => message.ChatId == chatId)
                // Read-only (provides faster query performance)
                .AsNoTracking()
                .ToListAsync();
        }

    }
}
