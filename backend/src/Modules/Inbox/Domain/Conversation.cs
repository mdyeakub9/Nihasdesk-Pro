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

    public DateTimeOffset LastMessageAt { get; private set; }

    /// <summary>Last customer message; unread markers compare against it.</summary>
    public DateTimeOffset? LastInboundAt { get; private set; }

    public string Preview { get; private set; } = string.Empty;

    public static Conversation Start(Guid tenantId, Guid contactId, string channel, DateTimeOffset now)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("ContactId is required.", nameof(contactId));
        }

        return new Conversation(tenantId, contactId, channel) { LastMessageAt = now };
    }

    /// <summary>A customer wrote: reopen when done; wait for the team when a human is answering.</summary>
    public void Received(Message message)
    {
        Touch(message);
        LastInboundAt = message.SentAt;
        if (Responder == Responder.Human)
        {
            Status = ConversationStatus.Waiting;
        }
        else if (Status == ConversationStatus.Done)
        {
            Status = ConversationStatus.Open;
        }
    }

    /// <summary>A team member replied: they take over, and the customer no longer waits.</summary>
    public void RepliedByTeam(Message message)
    {
        Touch(message);
        Responder = Responder.Human;
        if (Status == ConversationStatus.Waiting)
        {
            Status = ConversationStatus.Open;
        }
    }

    public void SetStatus(ConversationStatus status) => Status = status;

    public void TakeOver() => Responder = Responder.Human;

    public void ReturnToAi()
    {
        Responder = Responder.Ai;
        if (Status == ConversationStatus.Waiting)
        {
            Status = ConversationStatus.Open;
        }
    }

    public void SetAiEnabled(bool enabled) => AiEnabled = enabled;

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
            Preview = message.Text.Length <= PreviewMaxLength ? message.Text : message.Text[..PreviewMaxLength];
        }
    }
}
