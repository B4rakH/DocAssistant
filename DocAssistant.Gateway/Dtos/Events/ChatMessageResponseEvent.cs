using System.Text.Json.Serialization;

namespace DocAssistant.Gateway.Dtos.Events
{
    public record ChatMessageResponseEvent
    {
        [JsonPropertyName("chat_id")]
        public Guid ChatId { get; set; }

        [JsonPropertyName("message_id")]
        public Guid MessageId { get; set; }  // ID of the user message being responded to

        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;

        [JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("is_success")]
        public bool IsSuccess { get; set; } = true;

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("correlation_id")]
        public Guid? CorrelationId { get; set; }
    }
}
