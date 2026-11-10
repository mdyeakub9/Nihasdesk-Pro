using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.Modules.Inbox.Realtime;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

internal sealed partial class InboxIngest(
    InboxDbContext db,
    IContactDirectory contacts,
    IContactInterest interests,
    ICatalog catalog,
    ITenantContext tenant,
    InboxNotifier notifier,
    TimeProvider time,
    ILogger<InboxIngest> logger) : IInboxIngest
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
        var tap = kinds.Count == 0 ? await TapAsync(message.ActionPayload, conversation.Id, now, cancellationToken) : null;
        var stored = tap is not null
            ? Message.FromCustomerTap(tenant.TenantId, conversation.Id, tap.Value.Text, tap.Value.Action.Id, message.ExternalMessageId, sentAt)
            : kinds.Count == 0
                ? Message.FromCustomer(tenant.TenantId, conversation.Id, TextOf(message), message.ExternalMessageId, sentAt)
                : Message.FromCustomerWithMedia(tenant.TenantId, conversation.Id, message.Text, kinds[0], message.ExternalMessageId, sentAt);
        tap?.Action.MarkUsed(now);
        if (tap is { Action: { Action: RichAction.Buy, ProductId: { } wantedId } })
        {
            conversation.WantedProduct(wantedId, sentAt);
        }

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

        conversation.Received(stored, message.ConnectionId, now, codeAnswers: tap is { } t && RichAction.CodeAnswersForTeam.Contains(t.Action.Action));
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

        if (tap is { } tapped)
        {
            await RecordInterestAsync(contactId, tapped.Action, sentAt, cancellationToken);
        }

        await notifier.ConversationChangedAsync(tenant.TenantId, conversation.Id, cancellationToken);
        return new InboundResult(conversation.Id, stored.Id, Duplicate: false);
    }

    /// <summary>
    /// A tap on one of our buttons (ADR 0032 §3): the token must belong to this workspace (RLS and the
    /// query filter) and to the sender's conversation, must not be expired, and its product must still be
    /// active. Anything else is null and the customer's words are stored as plain text (SEC-36).
    /// </summary>
    private async Task<(MessageAction Action, string Text)?> TapAsync(string? payload, Guid conversationId, DateTimeOffset now, CancellationToken ct)
    {
        var action = await MessageActionLookup.FindAsync(db, payload, conversationId, ct);
        if (action is null || action.IsExpired(now))
        {
            return null;
        }

        string? productTitle = null;
        if (action.ProductId is { } productId)
        {
            if (await catalog.GetAsync(productId, ct) is not { } product)
            {
                return null;
            }

            productTitle = product.Title;
        }

        // "Lo voglio — Borsa Milano": what the customer tapped, with the product it was for.
        var text = productTitle is null ? action.Label : $"{action.Label} — {productTitle}";
        return (action, text.Length <= Message.TextMaxLength ? text : text[..Message.TextMaxLength]);
    }

    /// <summary>
    /// "Lo voglio", "Taglie e colori" and "Simili" add the product to the contact's products of interest
    /// (PRD 4.5). After the message is saved, so a repeated webhook doesn't count twice; if it fails the
    /// message still stands and only the interest is missing.
    /// </summary>
    private async Task RecordInterestAsync(Guid contactId, MessageAction action, DateTimeOffset at, CancellationToken ct)
    {
        if (action.ProductId is not { } productId || !InterestAction.All.Contains(action.Action))
        {
            return;
        }

        try
        {
            await interests.RecordAsync(contactId, productId, action.Action, at, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogInterestFailed(logger, action.Id, ex.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recording product interest for action {ActionId} failed ({Error})")]
    private static partial void LogInterestFailed(ILogger logger, Guid actionId, string error);

    /// <summary>A tap we can't use still reads as what the customer saw (the button's title).</summary>
    private static string TextOf(InboundMessage message) =>
        string.IsNullOrWhiteSpace(message.Text) && message.ActionPayload is not null ? "👆" : message.Text;

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
