using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Modules.Notifications.Realtime;

[Authorize(Roles = "Admin")]
public sealed class AdminOrdersHub : Hub
{
}
