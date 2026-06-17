using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace JIroad.Hubs;

[Authorize]
public sealed class CollaborationHub : Hub
{
}
