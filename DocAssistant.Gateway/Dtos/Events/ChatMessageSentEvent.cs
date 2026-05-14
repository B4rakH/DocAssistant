using System.Text.Json.Serialization;

namespace DocAssistant.Gateway.Dtos.Events
{
    public record ChatMessageSentEvent
    {
        [JsonPropertyName("chat_id")]
        public Guid ChatId { get; set; }

        [JsonPropertyName("message_id")]
        public Guid MessageId { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("correlation_id")]
        public Guid? CorrelationId { get; set; }
    }
}
