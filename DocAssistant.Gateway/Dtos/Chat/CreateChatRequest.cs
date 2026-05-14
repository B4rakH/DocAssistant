using System.ComponentModel.DataAnnotations;

namespace DocAssistant.Gateway.Dtos.Chat
{
    public record CreateChatRequest
    {
        [Required(ErrorMessage = "Chat name is required")]
        [MinLength(1, ErrorMessage = "Chat name cannot be empty")]
        [MaxLength(20, ErrorMessage = "Chat name cannot exceed 20 characters")]
        public string Name { get; set; } = string.Empty;
    }
}
