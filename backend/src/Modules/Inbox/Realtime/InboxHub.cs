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
        try
        {
            if (await team.FindAsync(userId, Context.ConnectionAborted) is null)
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, InboxNotifier.Group(tenantId), Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, InboxNotifier.MemberGroup(tenantId, userId), Context.ConnectionAborted);
        }
        catch (OperationCanceledException) when (Context.ConnectionAborted.IsCancellationRequested)
        {
            // The browser left while we were checking (page reload, tab closed): nothing to do.
            return;
        }

        await base.OnConnectedAsync();
    }
}

/// <summary>Tells a workspace's open dashboards that something changed. IDs only, no message text.</summary>
internal sealed class InboxNotifier(IHubContext<InboxHub> hub)
{
    public const string ConversationChanged = "conversationChanged";

    /// <summary>Someone <c>@mentioned</c> this member in a team note (PRD 4.4).</summary>
    public const string Mentioned = "mentioned";

    /// <summary>A team member is writing a reply in a conversation (PRD 4.4): IDs only, never the text.</summary>
    public const string Typing = "typing";

    public static string Group(Guid tenantId) => $"tenant:{tenantId:N}";

    /// <summary>One member's connections in one workspace (the same person may belong to several).</summary>
    public static string MemberGroup(Guid tenantId, Guid userId) => $"tenant:{tenantId:N}:user:{userId:N}";

    public Task ConversationChangedAsync(Guid tenantId, Guid conversationId, CancellationToken cancellationToken) =>
        hub.Clients.Group(Group(tenantId)).SendAsync(ConversationChanged, new { conversationId }, cancellationToken);

    /// <summary>The AI created an order request that waits for a person (ADR 0025): IDs only.</summary>
    public const string OrderRequested = "orderRequested";

    public Task OrderRequestedAsync(Guid tenantId, Guid conversationId, Guid orderId, CancellationToken cancellationToken) =>
        hub.Clients.Group(Group(tenantId)).SendAsync(OrderRequested, new { conversationId, orderId }, cancellationToken);

    public Task TypingAsync(Guid tenantId, Guid conversationId, Guid userId, CancellationToken cancellationToken) =>
        hub.Clients.Group(Group(tenantId)).SendAsync(Typing, new { conversationId, userId }, cancellationToken);

    public Task MentionedAsync(Guid tenantId, IReadOnlyList<Guid> userIds, Guid conversationId, Guid noteId, CancellationToken cancellationToken) =>
        hub.Clients.Groups(userIds.Select(id => MemberGroup(tenantId, id)).ToList())
            .SendAsync(Mentioned, new { conversationId, noteId }, cancellationToken);
}
