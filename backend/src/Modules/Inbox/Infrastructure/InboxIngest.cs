using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

internal sealed class InboxIngest(
    InboxDbContext db,
    IContactDirectory contacts,
    ITenantContext tenant,
    InboxNotifier notifier) : IInboxIngest
{
    public async Task<InboundResult> ReceiveAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!ChannelKind.All.Contains(message.Channel))
        {
            throw new ArgumentException($"Unknown channel '{message.Channel}'.", nameof(message));
        }

        // timestamptz needs UTC; channels may send any offset.
        var sentAt = message.SentAt.ToUniversalTime();
        var contactId = await contacts.FindOrCreateAsync(message.Channel, message.ExternalContactId, message.ContactName, cancellationToken);
        var conversation = await FindOrStartAsync(contactId, message.Channel, sentAt, cancellationToken);

        if (message.ExternalMessageId is { } externalId
            && await db.Messages.Where(m => m.ConversationId == conversation.Id && m.ExternalId == externalId)
                .Select(m => (Guid?)m.Id).SingleOrDefaultAsync(cancellationToken) is { } knownId)
        {
            return new InboundResult(conversation.Id, knownId, Duplicate: true);
        }

        var stored = Message.FromCustomer(tenant.TenantId, conversation.Id, message.Text, message.ExternalMessageId, sentAt);
        db.Messages.Add(stored);
        conversation.Received(stored);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception) && message.ExternalMessageId is not null)
        {
            // The same webhook delivery was processed in parallel.
            db.ChangeTracker.Clear();
            var existing = await db.Messages
                .Where(m => m.ConversationId == conversation.Id && m.ExternalId == message.ExternalMessageId)
                .Select(m => m.Id)
                .SingleAsync(cancellationToken);
            return new InboundResult(conversation.Id, existing, Duplicate: true);
        }

        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, cancellationToken);
        return new InboundResult(conversation.Id, stored.Id, Duplicate: false);
    }

    private async Task<Conversation> FindOrStartAsync(Guid contactId, string channel, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Conversations.SingleOrDefaultAsync(c => c.ContactId == contactId && c.Channel == channel, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var conversation = Conversation.Start(tenant.TenantId, contactId, channel, now);
        db.Conversations.Add(conversation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return conversation;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            return await db.Conversations.SingleAsync(c => c.ContactId == contactId && c.Channel == channel, cancellationToken);
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
