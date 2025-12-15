using System.ComponentModel.DataAnnotations;

namespace DocAssistant.Gateway.Dtos.Chat
{
    public record CreateChatRequest
    {
        [MaxLength(100)]
        public string Name { get; set; } = "New Chat";

        [Required]
        public List<IFormFile> Files { get; set; } = new();
    }
}
