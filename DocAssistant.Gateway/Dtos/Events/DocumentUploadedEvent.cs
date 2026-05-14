using System.Text.Json.Serialization;

namespace DocAssistant.Gateway.Dtos.Events;

public record DocumentUploadedEvent
{
    [JsonPropertyName("document_id")]
    public Guid DocumentId { get; set; }

    [JsonPropertyName("chat_id")]
    public Guid ChatId { get; set; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = null!;
    
    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }
    
    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }

    [JsonPropertyName("correlation_id")]
    public Guid? CorrelationId { get; set; }
}
