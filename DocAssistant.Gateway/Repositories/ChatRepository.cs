
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;

using DocAssistant.Gateway.Mappers;
using Microsoft.EntityFrameworkCore;

namespace DocAssistant.Gateway.Repositories
{
    public class ChatRepository(AppDbContext context) : IChatRepository
    {
        public async Task<List<Chat>> GetAllAsync()
        {
            return await context.Chats
                .ToListAsync();
        }

        public async Task<Chat?> GetByIdAsync(Guid chatId)
        {
            return await context.Chats
                .FirstOrDefaultAsync(c => c.Id == chatId);
        }

        public async Task<Chat> CreateAsync(string chatName, bool isTransaction = false)
        {            
            var newChat = new Chat
            {
                Name = chatName
            };

            await context.Chats.AddAsync(newChat);
            
            if (!isTransaction)
                await context.SaveChangesAsync();
            
            return newChat;
        }

        public async Task DeleteAsync(Guid chatId, bool isTransaction = false)
        {
            var chat = await context.Chats.FindAsync(chatId);

            if (chat == null)
                throw new KeyNotFoundException($"Chat with ID {chatId} not found.");

            context.Chats.Remove(chat);

            if (!isTransaction)
                await context.SaveChangesAsync();
        }

        public async Task<bool> ExistsByIdAsync(Guid chatId)
        {
            return await context.Chats.AnyAsync(c => c.Id == chatId);
        }
    }
}
