using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Inbox.Realtime;

/// <summary>
/// Pushes inbox changes to the dashboard (ADR 0016). Server → client only. A connection joins
/// only its own workspace's group, with the tenant taken from the session (CLAUDE.md rule 1),
/// and only while the user is still a member.
/// </summary>
[Authorize]
internal sealed class InboxHub(TenantContext tenant, ITeamDirectory team) : Hub
{
    public const string Path = "/api/v1/hubs/inbox";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (!Guid.TryParse(user?.FindFirstValue(TenantClaims.TenantId), out var tenantId)
            || !Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            Context.Abort();
            return;
        }

        // Hub calls get their own scope, so the request's tenant isn't set here yet.
        tenant.SetTenant(tenantId);
        if (await team.FindAsync(userId, Context.ConnectionAborted) is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, InboxNotifier.Group(tenantId), Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}

/// <summary>Tells a workspace's open dashboards that something changed. IDs only, no message text.</summary>
internal sealed class InboxNotifier(IHubContext<InboxHub> hub)
{
    public const string ConversationChanged = "conversationChanged";

    public static string Group(Guid tenantId) => $"tenant:{tenantId:N}";

    public Task ConversationChangedAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken) =>
        hub.Clients.Group(Group(tenantId)).SendAsync(ConversationChanged, new { conversationId }, cancellationToken);
}
