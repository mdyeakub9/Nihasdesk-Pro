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

/// <summary>One message or internal note in a conversation (ADR 0016).</summary>
internal sealed class Message : TenantEntity
{
    public const int TextMaxLength = 4000;
    public const int AuthorNameMaxLength = 100;
    public const int ExternalIdMaxLength = 200;
    public const int DeliveryErrorMaxLength = 100;

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
