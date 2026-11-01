using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>Tells Commerce whose chat an order comes from (ADR 0025); tenant-scoped, so other workspaces' IDs are null (SEC-06).</summary>
internal sealed class OrderConversations(InboxDbContext db) : IOrderConversations
{
    public async Task<Guid?> ContactIdAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.Conversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => (Guid?)c.ContactId)
            .SingleOrDefaultAsync(cancellationToken);
}
