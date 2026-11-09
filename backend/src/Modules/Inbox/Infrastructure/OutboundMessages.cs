using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

internal sealed class OutboundMessages(
    InboxDbContext db,
    IFileStore files,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time) : IOutboundMessages
{
    /// <summary>Delay before attempt 2, 3, …; afterwards the reply is marked failed.</summary>
    internal static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30),
    ];

    public async Task<IReadOnlyList<DueDelivery>> DueAsync(int limit, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        // SECURITY DEFINER lookup (migration AddChannelDelivery): IDs only, no message content.
        var rows = await db.Database
            .SqlQuery<DueRow>($"SELECT tenant_id, message_id FROM inbox.due_deliveries({now}, {limit})")
            .ToListAsync(cancellationToken);
        return rows.Select(r => new DueDelivery(r.TenantId, r.MessageId)).ToList();
    }

    public async Task<bool> DeliverAsync(
        Guid messageId,
        Func<OutboundMessage, CancellationToken, Task<DeliveryOutcome>> send,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Row lock for the duration of the send; RLS keeps it inside the current workspace.
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS value FROM inbox.messages WHERE id = {messageId} AND delivery = 'pending' FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);
        if (locked.Count == 0)
        {
            return false;
        }

        var message = await db.Messages.SingleAsync(m => m.Id == messageId, cancellationToken);
        var conversation = await db.Conversations.SingleAsync(c => c.Id == message.ConversationId, cancellationToken);
        var now = time.GetUtcNow();

        if (conversation.ChannelConnectionId is not { } connectionId)
        {
            message.DeliveryFailed("no_channel_connection", retryAt: null);
        }
        else if (!CustomerWindow.IsOpen(conversation.Channel, conversation.LastInboundAt, now))
        {
            message.DeliveryFailed("outside_window", retryAt: null);
        }
        else if (await ImageOfAsync(message, cancellationToken) is not { } image)
        {
            // The photo was removed (retention or erasure) before it could be sent.
            message.DeliveryFailed("media_missing", retryAt: null);
        }
        else
        {
            var outcome = await send(
                new OutboundMessage(
                    message.Id, conversation.Id, conversation.Channel, connectionId, conversation.ContactId, message.Text, message.DeliveryAttempts, image.Image),
                cancellationToken);

            if (outcome.ExternalId is { } externalId)
            {
                message.Delivered(externalId);
            }
            else
            {
                var attempt = message.DeliveryAttempts;
                var retryAt = outcome.Retry && attempt < Backoff.Length ? now + Backoff[attempt] : (DateTimeOffset?)null;
                message.DeliveryFailed(outcome.Error ?? "unknown", retryAt);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, cancellationToken);
        return true;
    }

    /// <summary>
    /// The photo of a team message, read from storage for this send. A text reply has none (<c>Image</c> is
    /// null); null means the message should have a photo but its file is gone.
    /// </summary>
    private async Task<PhotoToSend?> ImageOfAsync(Message message, CancellationToken ct)
    {
        if (message.Author != MessageAuthor.Agent)
        {
            return new PhotoToSend(null);
        }

        var attachment = await db.Attachments.AsNoTracking()
            .Where(a => a.MessageId == message.Id)
            .OrderBy(a => a.Position)
            .FirstOrDefaultAsync(ct);
        if (attachment is null)
        {
            return new PhotoToSend(null);
        }

        if (attachment is not { Status: AttachmentStatus.Saved, StorageKey: { } key, ContentType: { } contentType })
        {
            return null;
        }

        try
        {
            await using var stream = await files.OpenReadAsync(key, ct);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            return new PhotoToSend(new OutboundImage(buffer.ToArray(), contentType));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private sealed record PhotoToSend(OutboundImage? Image);

    private sealed record DueRow(Guid TenantId, Guid MessageId);
}
