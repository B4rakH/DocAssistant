using System.Text.Json.Serialization;

namespace DocAssistant.Gateway.Dtos.Events;

public record DocumentProcessedEvent
{
    [JsonPropertyName("document_id")]
    public Guid DocumentId { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }
}
