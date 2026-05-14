using DocAssistant.Gateway.Common.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DocAssistant.Gateway.Data.Models
{
    public class ChatMessage
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [ForeignKey(nameof(Chat))]
        public Guid ChatId { get; set; }

        [Required]
        public string Role { get; set; } = null!; // Should be either "USER" or "ASSISTANT"

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Navigation property
        public Chat Chat { get; set; } = null!;
    }
}
