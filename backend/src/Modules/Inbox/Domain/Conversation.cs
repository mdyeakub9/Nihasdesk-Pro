using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>ADR 0016: waiting = the customer waits for the team; done = resolved. Stored as lower-case text.</summary>
internal enum ConversationStatus
{
    Waiting,
    Open,
    Done,
}

/// <summary>Who answers the customer right now.</summary>
internal enum Responder
{
    Ai,
    Human,
}

/// <summary>Classification per conversation, editable by the team (PRD 4.5).</summary>
internal enum LeadStatus
{
    New,
    Hot,
    Customer,
    Support,
    Spam,
}

/// <summary>All messages with one contact on one channel (ADR 0016).</summary>
internal sealed class Conversation : TenantEntity
{
    public const int ChannelMaxLength = 20;
    public const int PreviewMaxLength = 200;

    private Conversation(Guid tenantId, Guid contactId, string channel)
        : base(tenantId)
    {
        ContactId = contactId;
        Channel = channel;
    }

    public Guid ContactId { get; private set; }

    public string Channel { get; private set; }

    public ConversationStatus Status { get; private set; } = ConversationStatus.Open;

    public Responder Responder { get; private set; } = Responder.Ai;

    public bool AiEnabled { get; private set; } = true;

    public LeadStatus LeadStatus { get; private set; } = LeadStatus.New;

    public Guid? AssigneeId { get; private set; }

    /// <summary>The connected channel account (e.g. Facebook Page) replies go out through; null for the website widget.</summary>
    public Guid? ChannelConnectionId { get; private set; }

    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>Last customer message; unread markers compare against it.</summary>
    public DateTimeOffset? LastInboundAt { get; private set; }

    public string Preview { get; private set; } = string.Empty;

    public static Conversation Start(Guid tenantId, Guid contactId, string channel, Guid? connectionId, DateTimeOffset now)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(contactId));
        }

        return new Conversation(tenantId, contactId, channel) { LastMessageAt = now, ChannelConnectionId = connectionId };
    }

    /// <summary>When the AI should answer; null when it shouldn't (ADR 0019).</summary>
    public DateTimeOffset? AiReplyDueAt { get; private set; }

    /// <summary>Wait this long after a customer message, so a burst of short messages gets one answer.</summary>
    public static readonly TimeSpan AiReplyDelay = TimeSpan.FromSeconds(4);

    public bool AiAnswers => Responder == Responder.Ai && AiEnabled;

    /// <summary>A customer wrote: reopen when done; wait for the team when a human is answering.</summary>
    public void Received(Message message, Guid? connectionId, DateTimeOffset now)
    {
        Touch(message);
        ChannelConnectionId = connectionId ?? ChannelConnectionId;
        LastInboundAt = message.SentAt;
        if (Responder == Responder.Human)
        {
            Status = ConversationStatus.Waiting;
        }
        else if (Status == ConversationStatus.Done)
        {
            Status = ConversationStatus.Open;
        }

        AiReplyDueAt = AiAnswers ? now + AiReplyDelay : null;
    }

    /// <summary>A team member replied: they take over, and the customer no longer waits.</summary>
    public void RepliedByTeam(Message message)
    {
        Touch(message);
        Responder = Responder.Human;
        AiReplyDueAt = null;
        if (Status == ConversationStatus.Waiting)
        {
            Status = ConversationStatus.Open;
        }
    }

    /// <summary>
    /// The shop told the customer about their order (ADR 0025): the chat shows it, but who answers
    /// the customer doesn't change.
    /// </summary>
    public void OrderUpdateSent(Message message) => Touch(message);

    /// <summary>The AI answered (ADR 0019).</summary>
    public void RepliedByAi(Message message)
    {
        if (message.Author != MessageAuthor.Ai)
        {
            throw new InvalidOperationException("Not an AI message.");
        }

        Touch(message);
    }

    /// <summary>The AI picked up the due reply; a new customer message makes it due again.</summary>
    public void AiReplyStarted() => AiReplyDueAt = null;

    /// <summary>The AI can't (or mustn't) answer: the team takes over and the customer waits for them.</summary>
    public void HandOffToTeam()
    {
        Responder = Responder.Human;
        Status = ConversationStatus.Waiting;
        AiReplyDueAt = null;
    }

    public void SetStatus(ConversationStatus status) => Status = status;

    public void TakeOver()
    {
        Responder = Responder.Human;
        AiReplyDueAt = null;
    }

    public void ReturnToAi()
    {
        Responder = Responder.Ai;
        if (Status == ConversationStatus.Waiting)
        {
            Status = ConversationStatus.Open;
        }
    }

    public void SetAiEnabled(bool enabled)
    {
        AiEnabled = enabled;
        if (!enabled)
        {
            AiReplyDueAt = null;
        }
    }

    public void SetLeadStatus(LeadStatus leadStatus) => LeadStatus = leadStatus;

    public void AssignTo(Guid? userId) => AssigneeId = userId;

    private void Touch(Message message)
    {
        if (message.ConversationId != Id)
        {
            throw new InvalidOperationException("Message belongs to another conversation.");
        }

        if (message.SentAt >= LastMessageAt)
        {
            LastMessageAt = message.SentAt;
            var preview = message.PreviewText;
            Preview = preview.Length <= PreviewMaxLength ? preview : preview[..PreviewMaxLength];
        }
    }
}
