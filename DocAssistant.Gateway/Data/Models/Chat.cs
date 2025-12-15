using System.ComponentModel.DataAnnotations;

namespace DocAssistant.Gateway.Data.Models
{
    public class Chat
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [MaxLength(20)]
        public string Name { get; set; } = "New Chat";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

        // Link to Many Documents
        public ICollection<ChatDocument> ChatDocuments { get; set; } = new List<ChatDocument>();
    }
}
