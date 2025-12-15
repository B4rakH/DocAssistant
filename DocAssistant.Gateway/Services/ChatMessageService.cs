using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Repositories;

namespace DocAssistant.Gateway.Services
{
    public class ChatMessageService(IChatMessageRepository chatMessageRepository) : IChatMessageService
    {
        public async Task<List<ChatMessage>> GetAllMesageAsync(Guid chatId)
        {
            return await chatMessageRepository.GetAllAsync(chatId);
        }
    }
}
