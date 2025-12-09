namespace DocAssistant.Gateway.Dtos.Events
{
    public class DocumentProcessedEvent
    {
        public Guid DocumentId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; } // If it exists
    }
}
