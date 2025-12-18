namespace DocAssistant.Gateway.Dtos.Events;

public record DocumentUploadedEvent
{
    public Guid DocumentId { get; set; }
    public string FilePath { get; set; } = null!;
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
}
