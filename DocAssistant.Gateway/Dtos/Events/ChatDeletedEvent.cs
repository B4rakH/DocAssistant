using System.Text.Json.Serialization;

namespace DocAssistant.Gateway.Dtos.Events;

public record ChatDeletedEvent
{
    [JsonPropertyName("chat_id")]
    public Guid ChatId { get; set; }

    [JsonPropertyName("correlation_id")]
    public Guid? CorrelationId { get; set; }
}
