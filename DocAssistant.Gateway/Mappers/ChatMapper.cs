using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.Chat;

namespace DocAssistant.Gateway.Mappers
{
    public static class ChatMapper
    {
        public static Chat ToModelFromCreate(this CreateChatRequest chatRequest) 
        {
            return new Chat
            {
                Name = chatRequest.Name,
            };
        }
    }
}
