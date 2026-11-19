using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Modules.Notifications.Realtime;

[Authorize(Roles = "Customer")]
public sealed class OrdersHub : Hub
{
}
