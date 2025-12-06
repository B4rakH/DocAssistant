using System.ComponentModel.DataAnnotations;

namespace DocAssistant.Gateway.Dtos.Chat
{
    public class CreateChatRequest
    {
        // OUser can name the room, or we default to "New Chat"
        [MaxLength(100)]
        public string Name { get; set; } = "New Chat";

        [Required]
        public List<IFormFile> Files { get; set; } = new();
    }
}
