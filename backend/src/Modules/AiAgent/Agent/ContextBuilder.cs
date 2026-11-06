using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Ingestion;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>What one AI call gets from the conversation. <see cref="NeedsSummary"/>: older messages aren't summarized yet.</summary>
internal sealed record ReplyContext(IReadOnlyList<AiHistoryMessage> Recent, string? Summary, bool NeedsSummary);

/// <summary>
/// Chooses the conversation part of the context within a token budget (ADR 0026 §4): the summary,
/// then the newest messages that fit. Same for the agent, the team's assistant and the test chat.
/// </summary>
internal static class ContextBuilder
{
    // Role and framing of one message.
    private const int MessageOverheadTokens = 4;

    /// <summary>The summary starts before messages drop out, so it is usually ready when they do.</summary>
    public static int SummaryStartBudget(AiOptions options) => options.RecentMessagesTokenBudget * 3 / 4;

    public static ReplyContext Build(IReadOnlyList<AiHistoryMessage> messages, ConversationMemory? memory, AiOptions options)
    {
        var older = Older(messages, SummaryStartBudget(options));
        return new ReplyContext(
            Recent(messages, options.RecentMessagesTokenBudget),
            memory?.Summary,
            HasUncovered(older, memory));
    }

    /// <summary>The newest messages that fit, oldest first. The last message is always kept, however long.</summary>
    public static IReadOnlyList<AiHistoryMessage> Recent(IReadOnlyList<AiHistoryMessage> messages, int budget) =>
        messages.Skip(messages.Count - FittingCount(messages, budget)).ToList();

    /// <summary>The messages before those that fit, oldest first.</summary>
    public static IReadOnlyList<AiHistoryMessage> Older(IReadOnlyList<AiHistoryMessage> messages, int budget) =>
        messages.Take(messages.Count - FittingCount(messages, budget)).ToList();

    private static int FittingCount(IReadOnlyList<AiHistoryMessage> messages, int budget)
    {
        var used = 0;
        var count = 0;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            used += Chunker.Tokens(messages[i].Text) + MessageOverheadTokens;
            if (used > budget && count > 0)
            {
                break;
            }

            count++;
        }

        return count;
    }

    /// <summary>Messages are in order, so everything up to the last covered one is in the summary.</summary>
    private static bool HasUncovered(IReadOnlyList<AiHistoryMessage> older, ConversationMemory? memory)
    {
        if (older.Count == 0)
        {
            return false;
        }

        if (memory?.CoveredUpToAt is not { } at)
        {
            return true;
        }

        // The last covered message is among the older ones: is anything after it? If not, it is newer than them all, or too old to be read.
        return older[^1].Id != memory.CoveredUpToMessageId
            && (older.Any(m => m.Id == memory.CoveredUpToMessageId) || older[^1].SentAt > at);
    }
}
