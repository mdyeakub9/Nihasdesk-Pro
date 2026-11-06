using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Features.Settings;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>
/// What the assistant wrote for the team. <see cref="Reply"/> is null when it was held back
/// (<see cref="Blocked"/>, SEC-38) or the provider failed (<see cref="Failed"/>).
/// </summary>
internal sealed record Suggestion(
    string? Summary,
    string? Reply,
    IReadOnlyList<TestSource> Sources,
    string? Blocked,
    string? Failed);

/// <summary>
/// The team's AI assistant (PRD 4.4, ADR 0023): a summary and a suggested reply for one conversation.
/// Same brain and reply check as the agent, read-only tools, the same history without notes. It
/// stores nothing but the metering and never sends: the team inserts and sends the text.
/// </summary>
internal sealed class SuggestionAgent(
    AgentBrain brain,
    AgentTools tools,
    AiSettingsStore settingsStore,
    IAiConversations conversations,
    IContactDirectory contacts,
    ILlmProvider llm,
    AiDbContext db,
    ITenantContext tenant,
    IOptions<AiOptions> options,
    TimeProvider time)
{
    public static readonly Error NotFound = Error.NotFound("conversation.not_found", "Conversation not found.");

    public static readonly Error AiOff = Error.Conflict("ai.off", "The AI is switched off for this workspace (Settings → AI).");

    public static readonly Error Limit = Error.Conflict("ai.suggest_limit", "The daily limit for AI suggestions in this workspace is reached.");

    public static readonly Error NoMessages = Error.Conflict("ai.no_messages", "There is nothing to suggest a reply to yet.");

    private AiOptions Options => options.Value;

    public async Task<Result<Suggestion>> SuggestAsync(Guid conversationId, string style, string teamLanguage, CancellationToken ct)
    {
        var conversation = await conversations.HistoryAsync(conversationId, Options.MaxHistoryMessages, ct);
        if (conversation is null)
        {
            return NotFound;
        }

        if (!llm.IsConfigured)
        {
            return AiSettingsErrors.NotConfigured;
        }

        var settings = await settingsStore.GetAsync(ct);
        if (!settings.Enabled)
        {
            return AiOff;
        }

        var dayAgo = time.GetUtcNow().AddDays(-1);
        if (await db.Runs.CountAsync(r => r.Outcome == RunOutcome.Suggested && r.CreatedAt > dayAgo, ct) >= Options.MaxSuggestionsPerWorkspacePerDay)
        {
            return Limit;
        }

        if (conversation.Messages.Count == 0)
        {
            return NoMessages;
        }

        var watch = Stopwatch.StartNew();
        var contact = (await contacts.GetAsync([conversation.ContactId], ct)).GetValueOrDefault(conversation.ContactId);
        var shop = await settingsStore.ShopNameAsync(ct);
        var prompt = Prompt.Assistant(shop, settings, FirstName(contact?.Name), contact?.Language, teamLanguage, style);
        tools.ContactId = conversation.ContactId;
        var memory = await db.Memory.AsNoTracking().SingleOrDefaultAsync(m => m.ConversationId == conversationId, ct);
        var context = ContextBuilder.Build(conversation.Messages, memory, Options);
        var thought = await brain.ThinkAsync(prompt, context.Recent, ct, readOnly: true, summary: context.Summary);

        db.Runs.Add(AiRun.Finish(
            tenant.TenantId, conversationId, Options.ReplyModel, thought.InputTokens, thought.OutputTokens, thought.Tools,
            RunOutcome.Suggested, thought.Blocked ?? thought.Failed, watch.Elapsed, thought.Sources,
            thought.Trace, Prompt.Version(Prompt.AssistantVersion, settings)));
        await db.SaveChangesAsync(ct);

        var sources = await AiSettingsEndpoints.SourcesAsync(thought.Sources, db, ct);
        if (thought.Tools.Any(t => t is "search_products" or "get_product_options"))
        {
            sources.Insert(0, new TestSource("catalog", null));
        }

        if (thought.Reply is null)
        {
            return Result.Success(new Suggestion(null, null, sources, thought.Blocked ?? (thought.Failed is null ? "no_reply" : null), thought.Failed));
        }

        var (summary, reply) = Split(thought.Reply);
        return Result.Success(new Suggestion(summary, reply, sources, reply is null ? "no_reply" : null, null));
    }

    /// <summary>"SUMMARY: … REPLY: …" → both parts; without markers the whole text is the reply.</summary>
    internal static (string? Summary, string? Reply) Split(string text)
    {
        var replyAt = text.IndexOf(Prompt.ReplyMarker, StringComparison.OrdinalIgnoreCase);
        if (replyAt < 0)
        {
            return (null, Clean(text));
        }

        var summary = text[..replyAt];
        var summaryAt = summary.IndexOf(Prompt.SummaryMarker, StringComparison.OrdinalIgnoreCase);
        if (summaryAt >= 0)
        {
            summary = summary[(summaryAt + Prompt.SummaryMarker.Length)..];
        }

        return (Clean(summary), Clean(text[(replyAt + Prompt.ReplyMarker.Length)..]));
    }

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Only the first name goes to the model (GDPR minimization).</summary>
    private static string? FirstName(string? name) =>
        name?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { Length: <= 40 } first ? first : null;
}
