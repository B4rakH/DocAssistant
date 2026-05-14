using Microsoft.AspNetCore.SignalR;

namespace DocAssistant.Gateway.Hubs
{
    public class ChatHub : Hub
    {
        // Client joins a specific chat room to receive real-time updates.
        public async Task JoinChat(Guid chatId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, chatId.ToString());
        }

        // Client leaves a chat room when navigating away.
        public async Task LeaveChat(Guid chatId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, chatId.ToString());
        }
    }
}
