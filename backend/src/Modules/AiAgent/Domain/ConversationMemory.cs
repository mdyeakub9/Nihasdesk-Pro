using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Domain;

/// <summary>
/// The running summary of one conversation's older messages (ADR 0026 §4), written by the summary
/// job. Team notes are never part of it; the contact is kept so it can be erased with them (SEC-45).
/// </summary>
internal sealed class ConversationMemory : TenantEntity
{
    public const int SummaryMaxLength = 2000;

    public const int SummaryMaxWords = 120;

    private ConversationMemory(Guid tenantId)
        : base(tenantId)
    {
    }

    public Guid ConversationId { get; private set; }

    public Guid ContactId { get; private set; }

    public string? Summary { get; private set; }

    /// <summary>The last message the summary covers; newer ones are not in it yet.</summary>
    public DateTimeOffset? CoveredUpToAt { get; private set; }

    public Guid? CoveredUpToMessageId { get; private set; }

    /// <summary>Set while the job has older messages to add; null when the summary is up to date.</summary>
    public DateTimeOffset? SummaryDueAt { get; private set; }

    public DateTimeOffset? SummaryLockUntil { get; private set; }

    public int SummaryAttempts { get; private set; }

    public static ConversationMemory Create(Guid tenantId, Guid conversationId, Guid contactId) =>
        new(tenantId) { ConversationId = conversationId, ContactId = contactId };

    public void MarkDue(DateTimeOffset now)
    {
        if (SummaryDueAt is null)
        {
            SummaryDueAt = now;
            SummaryLockUntil = null;
            SummaryAttempts = 0;
        }
    }

    public void Claim(DateTimeOffset lockUntil)
    {
        SummaryLockUntil = lockUntil;
        SummaryAttempts++;
    }

    public void Cover(string summary, DateTimeOffset upToAt, Guid upToMessageId)
    {
        Summary = summary;
        CoveredUpToAt = upToAt;
        CoveredUpToMessageId = upToMessageId;
        SummaryAttempts = 0;
        SummaryLockUntil = null;
    }

    /// <summary>Nothing more to add, or the job gave up until the next reply asks again.</summary>
    public void Done()
    {
        SummaryDueAt = null;
        SummaryLockUntil = null;
    }
}
