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
    InboxNotifier notifier,
    TimeProvider time) : IInboxIngest
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
        var conversation = await FindOrStartAsync(contactId, message.Channel, message.ConnectionId, sentAt, cancellationToken);

        if (message.ExternalMessageId is { } externalId
            && await db.Messages.Where(m => m.ConversationId == conversation.Id && m.ExternalId == externalId)
                .Select(m => (Guid?)m.Id).SingleOrDefaultAsync(cancellationToken) is { } knownId)
        {
            return new InboundResult(conversation.Id, knownId, Duplicate: true);
        }

        var now = time.GetUtcNow();
        var media = message.Attachments ?? [];
        var kinds = media.Select(a => KindOf(a.Kind)).ToList();
        var stored = kinds.Count == 0
            ? Message.FromCustomer(tenant.TenantId, conversation.Id, message.Text, message.ExternalMessageId, sentAt)
            : Message.FromCustomerWithMedia(tenant.TenantId, conversation.Id, message.Text, kinds[0], message.ExternalMessageId, sentAt);
        db.Messages.Add(stored);
        // Saved with the message, so a repeated webhook can't add them twice (it stops at the check above).
        for (var i = 0; i < media.Count; i++)
        {
            var attachment = MessageAttachment.Incoming(tenant.TenantId, stored.Id, i, kinds[i], Usable(media[i].SourceUrl), now);
            if (attachment.SourceUrl is null)
            {
                attachment.Failed(AttachmentFailure.Expired);
            }

            db.Attachments.Add(attachment);
        }

        conversation.Received(stored, message.ConnectionId, now);
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

    private static AttachmentKind KindOf(string kind) => kind switch
    {
        InboundAttachmentKind.Image => AttachmentKind.Image,
        InboundAttachmentKind.Audio => AttachmentKind.Audio,
        InboundAttachmentKind.Video => AttachmentKind.Video,
        InboundAttachmentKind.File => AttachmentKind.File,
        InboundAttachmentKind.Sticker => AttachmentKind.Sticker,
        _ => throw new ArgumentException($"Unknown attachment kind '{kind}'.", nameof(kind)),
    };

    private static string? Usable(string? url) =>
        string.IsNullOrEmpty(url) || url.Length > MessageAttachment.SourceUrlMaxLength ? null : url;

    private async Task<Conversation> FindOrStartAsync(Guid contactId, string channel, Guid? connectionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.Conversations.SingleOrDefaultAsync(c => c.ContactId == contactId && c.Channel == channel, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var conversation = Conversation.Start(tenant.TenantId, contactId, channel, connectionId, now);
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
