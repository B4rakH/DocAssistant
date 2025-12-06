namespace DocAssistant.Gateway.Data.Models
{
    public class ChatDocument
    {
        public Guid ChatId { get; set; }
        public Chat Chat { get; set; } = null!;

        public Guid DocumentId { get; set; }
        public Document Document { get; set; } = null!;

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
