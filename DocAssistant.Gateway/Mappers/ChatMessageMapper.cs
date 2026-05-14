using DocAssistant.Gateway.Common.Enums;
using DocAssistant.Gateway.Data.Models;
using DocAssistant.Gateway.Dtos.ChatMessage;

namespace DocAssistant.Gateway.Mappers
{
    public static class ChatMessageMapper
    {
        public static ChatMessage ToModelFromRequest(this ChatMessageRequest messageRequest, Guid chatId, string role)
        {
            return new ChatMessage
            {
                ChatId = chatId,
                Role = role,  // Role is passed as parameter (set server-side)
                Content = messageRequest.Content,
            };
        }
    }
}
