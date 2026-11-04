namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>A conversation waiting for an AI answer, in some workspace (IDs only; ADR 0019).</summary>
public sealed record DueAiReply(Guid TenantId, Guid ConversationId);

/// <summary>One message of the history the AI may see: no internal notes, no team names.</summary>
public sealed record AiHistoryMessage(string Author, string Text, DateTimeOffset SentAt);

/// <summary>What the AI needs to answer one conversation, already limited to what it may see (GDPR).</summary>
public sealed record AiConversation(
    Guid ConversationId,
    Guid ContactId,
    string Channel,
    bool AiHasReplied,
    IReadOnlyList<AiHistoryMessage> Messages);

/// <summary>How the AI module reads and answers conversations; the Inbox keeps the rules.</summary>
public interface IAiConversations
{
    /// <summary>Due conversations of all workspaces, oldest first. Needs no tenant.</summary>
    Task<IReadOnlyList<DueAiReply>> DueAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// In the conversation's workspace: takes the due reply (so no other processor does) and returns
    /// the context, or null when it's no longer due or the AI no longer answers it.
    /// </summary>
    Task<AiConversation?> ClaimAsync(Guid conversationId, int historySize, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the AI's messages (e.g. disclosure, then the answer) and queues them for the channel.
    /// Returns false when the team took over meanwhile or the channel's 24-hour window is closed.
    /// </summary>
    Task<bool> ReplyAsync(Guid conversationId, IReadOnlyList<string> messages, CancellationToken cancellationToken);

    /// <summary>Like <see cref="ReplyAsync"/>, with the stored messages' IDs in order; null when nothing was sent.</summary>
    Task<IReadOnlyList<Guid>?> ReplyWithIdsAsync(Guid conversationId, IReadOnlyList<string> messages, CancellationToken cancellationToken);

    /// <summary>
    /// The team's AI assistant (ADR 0023): the same context the AI agent would get — no internal
    /// notes, no team names — without claiming or changing anything. Null when not in this workspace.
    /// </summary>
    Task<AiConversation?> HistoryAsync(Guid conversationId, int historySize, CancellationToken cancellationToken);

    /// <summary>Hands the conversation to the team, with a note for them saying why.</summary>
    Task HandOffAsync(Guid conversationId, string teamNote, CancellationToken cancellationToken);
}
