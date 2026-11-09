using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>Who wrote a message. <c>Agent</c> is a team member (the AI is <c>Ai</c>).</summary>
internal enum MessageAuthor
{
    Customer,
    Ai,
    Agent,
}

/// <summary>Sending a team reply to the customer's channel (ADR 0017). Stored as lower-case text.</summary>
internal enum DeliveryStatus
{
    /// <summary>Nothing to send: customer messages, notes, the website widget.</summary>
    None,
    Pending,
    Sent,
    Failed,
}

/// <summary>What a customer message is (ADR 0032 §3). Stored as lower-case text.</summary>
internal enum MessageKind
{
    Text,

    /// <summary>The customer tapped a button or quick reply; <see cref="Message.Text"/> is its label.</summary>
    Tap,
}

/// <summary>One message or internal note in a conversation (ADR 0016).</summary>
internal sealed class Message : TenantEntity
{
    public const int TextMaxLength = 4000;
    public const int AuthorNameMaxLength = 100;
    public const int ExternalIdMaxLength = 200;
    public const int DeliveryErrorMaxLength = 100;
    public const int RichKindMaxLength = 20;

    private string? _mediaPreview;

    private Message(Guid tenantId, Guid conversationId, MessageAuthor author, string text, DateTimeOffset sentAt)
        : base(tenantId)
    {
        ConversationId = conversationId;
        Author = author;
        Text = text;
        SentAt = sentAt;
    }

    public Guid ConversationId { get; private set; }

    public MessageAuthor Author { get; private set; }

    /// <summary>Team member who wrote it (agent messages and notes).</summary>
    public Guid? AuthorUserId { get; private set; }

    /// <summary>Name at the time of writing, so history stays readable after someone leaves.</summary>
    public string? AuthorName { get; private set; }

    public string Text { get; private set; }

    /// <summary>Team-only note: never sent to the customer or the AI.</summary>
    public bool Internal { get; private set; }

    /// <summary>The channel's message ID, for de-duplicating webhook retries.</summary>
    public string? ExternalId { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public DeliveryStatus Delivery { get; private set; }

    public int DeliveryAttempts { get; private set; }

    public DateTimeOffset? NextDeliveryAt { get; private set; }

    /// <summary>Short error code from the channel, never message content.</summary>
    public string? DeliveryError { get; private set; }

    public static Message FromCustomer(Guid tenantId, Guid conversationId, string text, string? externalId, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Customer, CheckText(text), sentAt) { ExternalId = externalId };

    /// <summary>A customer message with media (ADR 0031): the text may be empty, then the list shows what kind it was.</summary>
    public static Message FromCustomerWithMedia(Guid tenantId, Guid conversationId, string text, AttachmentKind firstKind, string? externalId, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Customer, string.IsNullOrWhiteSpace(text) ? string.Empty : CheckText(text), sentAt)
        {
            ExternalId = externalId,
            _mediaPreview = $"[{AttachmentText.Of(firstKind)}]",
        };

    /// <summary>What the conversation list shows for this message.</summary>
    public string PreviewText => Text.Length > 0 ? Text : _mediaPreview ?? string.Empty;

    /// <summary>
    /// No longer used: pinning was removed (ADR 0021 update). The columns stay until a later
    /// expand → contract cleanup drops them (docs/standards/database.md).
    /// </summary>
    public DateTimeOffset? PinnedAt { get; private set; }

    public Guid? PinnedByUserId { get; private set; }

    /// <summary>When a team member marked this note as resolved (dimmed in the Notes section).</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    /// <summary>Team members <c>@mentioned</c> in a note; checked against the workspace's members.</summary>
    public List<Guid> MentionedUserIds { get; private set; } = [];

    public static Message FromTeam(Guid tenantId, Guid conversationId, Guid userId, string authorName, string text, bool isInternal, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Agent, CheckText(text), sentAt)
        {
            AuthorUserId = userId,
            AuthorName = authorName.Length <= AuthorNameMaxLength ? authorName : authorName[..AuthorNameMaxLength],
            Internal = isInternal,
        };

    /// <summary>A catalog product the team sends (ADR 0024): the channel gets <paramref name="text"/>, the dashboard shows the card.</summary>
    public static Message FromTeamProduct(Guid tenantId, Guid conversationId, Guid userId, string authorName, string text, ProductSnapshot product, DateTimeOffset sentAt)
    {
        var message = FromTeam(tenantId, conversationId, userId, authorName, text, isInternal: false, sentAt);
        message.Product = product;
        return message;
    }

    /// <summary>A photo the team sends from the + menu (ADR 0031 §6): no text, the list shows <c>[image]</c>.</summary>
    public static Message FromTeamImage(Guid tenantId, Guid conversationId, Guid userId, string authorName, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Agent, string.Empty, sentAt)
        {
            AuthorUserId = userId,
            AuthorName = authorName.Length <= AuthorNameMaxLength ? authorName : authorName[..AuthorNameMaxLength],
            _mediaPreview = $"[{AttachmentText.Of(AttachmentKind.Image)}]",
        };

    /// <summary>Set for product cards sent from the + menu.</summary>
    public ProductSnapshot? Product { get; private set; }

    public MessageKind Kind { get; private set; }

    /// <summary>For a tap: the button or quick reply in <c>inbox.message_actions</c> it came from.</summary>
    public Guid? ActionId { get; private set; }

    /// <summary>One of <c>Contracts.RichKind</c> when the message carries rich content (ADR 0032 §1).</summary>
    public string? RichKind { get; private set; }

    /// <summary>The channel-neutral content (product ids, actions) as JSON; never prices.</summary>
    public string? RichContent { get; private set; }

    /// <summary>What was actually sent (titles, prices, labels read from the catalog at send time) as JSON, for the team's view.</summary>
    public string? RichRendered { get; private set; }

    /// <summary>The channel refused the rich message, so a numbered text list went instead.</summary>
    public bool FallbackText { get; private set; }

    /// <summary>A tap on one of our buttons (ADR 0032 §3); <paramref name="label"/> is what the customer saw.</summary>
    public static Message FromCustomerTap(Guid tenantId, Guid conversationId, string label, Guid actionId, string? externalId, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Customer, CheckText(label), sentAt)
        {
            ExternalId = externalId,
            Kind = MessageKind.Tap,
            ActionId = actionId,
        };

    /// <summary>Adds rich content to a reply before it is queued; <paramref name="json"/> is the serialized contract.</summary>
    public void AttachRich(string kind, string json)
    {
        if (Author == MessageAuthor.Customer || Internal)
        {
            throw new InvalidOperationException("Only replies to the customer carry rich content.");
        }

        RichKind = kind;
        RichContent = json;
    }

    /// <summary>Records what the channel sender rendered from the catalog, and whether it fell back to text.</summary>
    public void Rendered(string renderedJson, bool fallbackText)
    {
        RichRendered = renderedJson;
        FallbackText = fallbackText;
    }

    /// <summary>A team-only note (PRD 4.4). Never delivered to the channel or given to the AI.</summary>
    public static Message TeamNote(Guid tenantId, Guid conversationId, Guid userId, string authorName, string text, IEnumerable<Guid> mentions, DateTimeOffset sentAt)
    {
        var note = FromTeam(tenantId, conversationId, userId, authorName, text, isInternal: true, sentAt);
        note.MentionedUserIds = mentions.Distinct().ToList();
        return note;
    }

    /// <summary>An AI reply to the customer (ADR 0019).</summary>
    public static Message FromAi(Guid tenantId, Guid conversationId, string text, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Ai, CheckText(text), sentAt);

    /// <summary>A note from the AI to the team, e.g. why it handed over. Never sent to the customer.</summary>
    public static Message AiNote(Guid tenantId, Guid conversationId, string text, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Ai, CheckText(text), sentAt) { Internal = true };

    /// <summary>Marks the note as done; it is dimmed and grouped under "Resolved".</summary>
    public void Resolve(Guid userId, DateTimeOffset now)
    {
        if (!Internal)
        {
            throw new InvalidOperationException("Only notes can be resolved.");
        }

        ResolvedAt ??= now;
        ResolvedByUserId ??= userId;
    }

    public void Reopen()
    {
        ResolvedAt = null;
        ResolvedByUserId = null;
    }

    public void QueueDelivery(DateTimeOffset now)
    {
        if (Author == MessageAuthor.Customer || Internal)
        {
            throw new InvalidOperationException("Only replies to the customer are delivered.");
        }

        Delivery = DeliveryStatus.Pending;
        NextDeliveryAt = now;
    }

    public void Delivered(string externalId)
    {
        Delivery = DeliveryStatus.Sent;
        ExternalId = externalId.Length <= ExternalIdMaxLength ? externalId : externalId[..ExternalIdMaxLength];
        NextDeliveryAt = null;
        DeliveryError = null;
    }

    /// <summary>Counts the attempt; retries at <paramref name="retryAt"/> or gives up when it is null.</summary>
    public void DeliveryFailed(string error, DateTimeOffset? retryAt)
    {
        DeliveryAttempts++;
        DeliveryError = error.Length <= DeliveryErrorMaxLength ? error : error[..DeliveryErrorMaxLength];
        NextDeliveryAt = retryAt;
        if (retryAt is null)
        {
            Delivery = DeliveryStatus.Failed;
        }
    }

    private static string CheckText(string text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > TextMaxLength)
        {
            throw new ArgumentException("Message text must be 1 to 4,000 characters.", nameof(text));
        }

        return trimmed;
    }
}

/// <summary>How far one team member has read a conversation (unread is per member, ADR 0016).</summary>
internal sealed class ReadMarker : TenantEntity
{
    private ReadMarker(Guid tenantId, Guid conversationId, Guid userId)
        : base(tenantId)
    {
        ConversationId = conversationId;
        UserId = userId;
    }

    public Guid ConversationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset ReadAt { get; private set; }

    public static ReadMarker Create(Guid tenantId, Guid conversationId, Guid userId, DateTimeOffset readAt) =>
        new(tenantId, conversationId, userId) { ReadAt = readAt };

    public void MarkRead(DateTimeOffset readAt)
    {
        if (readAt > ReadAt)
        {
            ReadAt = readAt;
        }
    }
}
