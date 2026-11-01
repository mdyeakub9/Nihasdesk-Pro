using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Domain;

internal enum GapStatus
{
    Open,
    Answered,
    Dismissed,
}

/// <summary>
/// A customer question the AI couldn't answer (ADR 0019). The team answers it once; the answer
/// becomes a <see cref="TaughtAnswer"/> the AI uses from then on.
/// </summary>
internal sealed class KnowledgeGap : TenantEntity
{
    public const int QuestionMaxLength = 1000;

    private KnowledgeGap(Guid tenantId, Guid conversationId, string question)
        : base(tenantId)
    {
        ConversationId = conversationId;
        Question = question;
    }

    public Guid ConversationId { get; private set; }

    public string Question { get; private set; }

    public GapStatus Status { get; private set; } = GapStatus.Open;

    public Guid? AnswerId { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public static KnowledgeGap Record(Guid tenantId, Guid conversationId, string question) =>
        new(tenantId, conversationId, Shorten(question, QuestionMaxLength));

    public void Answered(Guid answerId, Guid userId)
    {
        Status = GapStatus.Answered;
        AnswerId = answerId;
        ResolvedByUserId = userId;
    }

    public void Dismiss(Guid userId)
    {
        Status = GapStatus.Dismissed;
        ResolvedByUserId = userId;
    }

    internal static string Shorten(string text, int max)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Text is required.", nameof(text));
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

/// <summary>A question and the team's answer, used by the AI's <c>search_knowledge</c> tool.</summary>
internal sealed class TaughtAnswer : TenantEntity
{
    public const int QuestionMaxLength = 1000;
    public const int AnswerMaxLength = 4000;

    private TaughtAnswer(Guid tenantId, string question, string answer, Guid updatedByUserId)
        : base(tenantId)
    {
        Question = question;
        Answer = answer;
        UpdatedByUserId = updatedByUserId;
    }

    public string Question { get; private set; }

    public string Answer { get; private set; }

    /// <summary>Switched-off answers stay for reference but the AI doesn't use them.</summary>
    public bool Active { get; private set; } = true;

    public Guid UpdatedByUserId { get; private set; }

    public static TaughtAnswer Create(Guid tenantId, string question, string answer, Guid userId) =>
        new(tenantId, KnowledgeGap.Shorten(question, QuestionMaxLength), KnowledgeGap.Shorten(answer, AnswerMaxLength), userId);

    public void Update(string question, string answer, bool active, Guid userId)
    {
        Question = KnowledgeGap.Shorten(question, QuestionMaxLength);
        Answer = KnowledgeGap.Shorten(answer, AnswerMaxLength);
        Active = active;
        UpdatedByUserId = userId;
    }
}

internal enum RunOutcome
{
    Replied,
    HandedOff,
    Blocked,
    LimitReached,
    Failed,
    Dropped,

    /// <summary>A draft for the team from the AI assistant (ADR 0023); nothing was sent.</summary>
    Suggested,
}

/// <summary>
/// One AI turn, for metering and "why did the AI do this" (SEC-40): model, tokens, tool names and
/// outcome. No prompt or reply text: the reply is the inbox message.
/// </summary>
internal sealed class AiRun : TenantEntity
{
    public const int ModelMaxLength = 100;
    public const int DetailMaxLength = 200;

    private AiRun(Guid tenantId, Guid conversationId)
        : base(tenantId)
    {
        ConversationId = conversationId;
    }

    public Guid ConversationId { get; private set; }

    public string Model { get; private set; } = string.Empty;

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public List<string> Tools { get; private set; } = [];

    /// <summary>What the answer was based on (PRD 4.7), as codes: <c>shipping</c>, <c>policy:returns</c>, <c>answer:{id}</c>.</summary>
    public List<string> Sources { get; private set; } = [];

    public RunOutcome Outcome { get; private set; }

    /// <summary>Short reason code, e.g. <c>price_not_from_tool</c>; never customer text.</summary>
    public string? Detail { get; private set; }

    public int DurationMs { get; private set; }

    public static AiRun Finish(
        Guid tenantId, Guid conversationId, string model, int inputTokens, int outputTokens,
        IEnumerable<string> tools, RunOutcome outcome, string? detail, TimeSpan duration, IEnumerable<string>? sources = null) =>
        new(tenantId, conversationId)
        {
            Model = model.Length <= ModelMaxLength ? model : model[..ModelMaxLength],
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Tools = [.. tools],
            Sources = [.. (sources ?? []).Distinct().Take(20)],
            Outcome = outcome,
            Detail = detail is null ? null : detail.Length <= DetailMaxLength ? detail : detail[..DetailMaxLength],
            DurationMs = (int)Math.Min(int.MaxValue, duration.TotalMilliseconds),
        };
}
