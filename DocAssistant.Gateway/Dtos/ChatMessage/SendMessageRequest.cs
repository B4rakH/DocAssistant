using System.ComponentModel.DataAnnotations;

namespace DocAssistant.Gateway.Dtos.ChatMessage
{
    public class SendMessageRequest
    {
        [Required]
        public Guid DocumentId { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Message cannot be empty")]
        public string Content { get; set; } = string.Empty;
    }
}
