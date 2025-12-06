using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DocAssistant.Gateway.Data.Models
{
    public class ChatMessage
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        // Links to the Chat Room (not the file directly)
        public Guid ChatId { get; set; }

        [ForeignKey("ChatId")]
        public Chat? Chat { get; set; }

        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = string.Empty; // "user" or "assistant"

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
