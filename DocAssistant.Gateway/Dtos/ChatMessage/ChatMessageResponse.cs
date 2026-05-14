using DocAssistant.Gateway.Common.Enums;
using System;

namespace DocAssistant.Gateway.Dtos.ChatMessage
{
    public class ChatMessageResponse
    {
        public Guid MessageId { get; set; }
        public Guid ChatId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string Role { get; set; } = MessageRole.Assistant;
        public double? ConfidenceScore { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
