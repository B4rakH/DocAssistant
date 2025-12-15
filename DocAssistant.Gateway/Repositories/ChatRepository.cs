
using DocAssistant.Gateway.Data;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;
using DocAssistant.Gateway.Mappers;

namespace DocAssistant.Gateway.Repositories
{
    public class ChatRepository(AppDbContext context) : IChatRepository
    {
        public async Task<Chat> CreateAsync(CreateChatRequest createChat, bool isTransaction = false)
        {
            var newChat = createChat.ToModelFromCreate();
            
            await context.Chats.AddAsync(newChat);
            
            if (!isTransaction)
                await context.SaveChangesAsync();
            
            return newChat;
        }
    }
}
