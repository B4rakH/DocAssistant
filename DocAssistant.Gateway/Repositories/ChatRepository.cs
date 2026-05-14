using DocAssistant.Gateway.Common.Enums;
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
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Chat?> GetByIdAsync(Guid chatId)
        {
            return await context.Chats
                .AsNoTracking()
                .Include(c => c.Messages)
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
            return await context.Chats
                .AsNoTracking()
                .AnyAsync(c => c.Id == chatId);
        }

        public async Task<(bool ChatExists, bool AllDocumentsCompleted)> ValidateChatAndDocumentsAsync(Guid chatId)
        {
            var chatData = await context.Chats
                .AsNoTracking()
                .Where(c => c.Id == chatId)
                .Select(c => new
                {
                    Exists = true,
                    HasDocuments = c.Documents.Any(),
                    AllCompleted = c.Documents.All(d => d.Status == DocumentStatus.Completed)
                })
                .FirstOrDefaultAsync();

            if (chatData == null)
                return (false, false);

            // If chat has no documents, allow chatting (business decision)
            if (!chatData.HasDocuments)
                return (true, true);

            return (true, chatData.AllCompleted);
        }
    }
}
