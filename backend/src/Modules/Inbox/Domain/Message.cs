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

    public static Message FromTeam(Guid tenantId, Guid conversationId, Guid userId, string authorName, string text, bool isInternal, DateTimeOffset sentAt) =>
        new(tenantId, conversationId, MessageAuthor.Agent, CheckText(text), sentAt)
        {
            AuthorUserId = userId,
            AuthorName = authorName.Length <= AuthorNameMaxLength ? authorName : authorName[..AuthorNameMaxLength],
            Internal = isInternal,
        };

    public void QueueDelivery(DateTimeOffset now)
    {
        if (Author != MessageAuthor.Agent || Internal)
        {
            throw new InvalidOperationException("Only team replies are delivered.");
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
