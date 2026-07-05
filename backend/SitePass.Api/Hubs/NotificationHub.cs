using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace SitePass.Api.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            Console.WriteLine($"User {userId} connected to NotificationHub. ConnectionId: {Context.ConnectionId}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            Console.WriteLine($"User {userId} disconnected from NotificationHub. ConnectionId: {Context.ConnectionId}");
            await base.OnDisconnectedAsync(exception);
        }

        // Method to send a message to a specific resident
        public async Task SendNotificationToUser(string userId, string title, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveNotification", new { Title = title, Message = message, Timestamp = DateTime.UtcNow });
        }
    }
}
