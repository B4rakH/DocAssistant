namespace DocAssistant.Gateway.Dtos.ChatMessage
{
    public class ChatMessageResponse
    {
        public string Content { get; set; } = string.Empty;
        public string Role { get; set; } = "assistant"; // "user" or "assistant"

    }
}
