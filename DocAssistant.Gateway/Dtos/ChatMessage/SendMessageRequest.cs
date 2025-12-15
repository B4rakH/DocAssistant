using System.ComponentModel.DataAnnotations;

namespace DocAssistant.Gateway.Dtos.ChatMessage
{
    public record SendMessageRequest
    {
        [Required]
        [MinLength(1, ErrorMessage = "Message cannot be empty")]
        [MaxLength(500, ErrorMessage = "Message cannot exceed 5000 characters")]
        public string Content { get; set; } = string.Empty;
    }
}
