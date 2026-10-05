using Kiosk.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace Kiosk.Server.Hubs;

// Messages contain only an invalidation notice. Credentials are never accepted in the query string.
public sealed class DeploymentHub(DeploymentStore store) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();
        bool panel = Context.User?.Identity?.IsAuthenticated == true;
        string? station = store.Authenticate(http?.Request.Headers["X-Deployment-Credential"].FirstOrDefault());
        if (!panel && station is null) { Context.Abort(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, panel ? "deployment-panel" : "deployment-stations");
        await base.OnConnectedAsync();
    }
}
