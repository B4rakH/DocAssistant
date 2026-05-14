using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Repositories
{
    public class ChatMessageRepository(AppDbContext context) : IChatMessageRepository
    {
        public async Task CreateAsync(ChatMessage chatMessage)
        {
            await context.ChatMessages.AddAsync(chatMessage);
            await context.SaveChangesAsync();
        }

        public async Task<List<ChatMessage>> GetAllAsync(Guid chatId)
        {
            return await context.ChatMessages
                .Where(message => message.ChatId == chatId)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task DeleteAsync(Guid messageId)
        {
            var message = await context.ChatMessages.FindAsync(messageId);
            
            if (message != null)
            {
                context.ChatMessages.Remove(message);
                await context.SaveChangesAsync();
            }
        }

        public async Task<bool> ExistsResponseForMessageAsync(Guid chatId, DateTime timestamp)
        {
            return await context.ChatMessages
                .AnyAsync(m => m.ChatId == chatId
                    && m.Role == "Assistant"
                    && m.Timestamp == timestamp);
        }
    }
}
