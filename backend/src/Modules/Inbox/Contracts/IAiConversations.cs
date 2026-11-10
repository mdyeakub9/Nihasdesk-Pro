namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>A conversation waiting for an AI answer, in some workspace (IDs only; ADR 0019).</summary>
public sealed record DueAiReply(Guid TenantId, Guid ConversationId);

/// <summary>One message of the history the AI may see: no internal notes, no team names.</summary>
/// <param name="HasMedia">The message has a photo, voice note or file the AI can't see (ADR 0031).</param>
/// <param name="Tap">Set when the customer tapped one of our buttons (ADR 0032 §3); already checked by the Inbox.</param>
public sealed record AiHistoryMessage(string Author, string Text, DateTimeOffset SentAt, Guid Id = default, bool HasMedia = false, AiTap? Tap = null)
{
    /// <summary>The text of a message that is only media; also what older messages stored.</summary>
    public const string MediaOnlyText = "[attachment]";
}

/// <summary>
/// What a tap acts on, from <c>inbox.message_actions</c> of this workspace and conversation (never from
/// the payload). <paramref name="FromTeam"/>: the button was on a message a team member sent.
/// </summary>
/// <param name="Action">One of <see cref="RichAction"/>.</param>
public sealed record AiTap(string Action, Guid? ProductId, Guid? VariantId, string? Value, bool FromTeam);

/// <summary>What the AI needs to answer one conversation, already limited to what it may see (GDPR).</summary>
/// <param name="CodeOnly">
/// The team has the conversation, but its last message is a tap code still answers (options of a
/// product, ADR 0032 §7): only code may reply, never the model.
/// </param>
public sealed record AiConversation(
    Guid ConversationId,
    Guid ContactId,
    string Channel,
    bool AiHasReplied,
    IReadOnlyList<AiHistoryMessage> Messages,
    bool CodeOnly = false);

/// <summary>One message the AI or tap code sends: <paramref name="Text"/> is what the history and the team's list read.</summary>
/// <param name="Rich">A carousel, quick replies or buttons sent with it (ADR 0032 §5); ids only, checked by the Inbox.</param>
public sealed record AiReply(string Text, RichContent? Rich = null);

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
    /// Like <see cref="ReplyWithIdsAsync"/>, with rich content. <paramref name="codeOnly"/>: code answers a
    /// tap while the team has the conversation (<see cref="AiConversation.CodeOnly"/>); allowed only then.
    /// </summary>
    Task<IReadOnlyList<Guid>?> ReplyRichAsync(Guid conversationId, IReadOnlyList<AiReply> messages, bool codeOnly, CancellationToken cancellationToken);

    /// <summary>
    /// The team's AI assistant (ADR 0023): the same context the AI agent would get — no internal
    /// notes, no team names — without claiming or changing anything. Null when not in this workspace.
    /// </summary>
    Task<AiConversation?> HistoryAsync(Guid conversationId, int historySize, CancellationToken cancellationToken);

    /// <summary>Hands the conversation to the team, with a note for them saying why.</summary>
    Task HandOffAsync(Guid conversationId, string teamNote, CancellationToken cancellationToken);
}

/// <summary>Older messages for the running summary (ADR 0026 §4). Needs only the database, so the Workers host can use it.</summary>
public interface IAiMessageReader
{
    /// <summary>The messages the AI may see after the given one, oldest first: no internal notes, no team names.</summary>
    Task<IReadOnlyList<AiHistoryMessage>> AfterAsync(Guid conversationId, DateTimeOffset? afterSentAt, Guid? afterId, int limit, CancellationToken cancellationToken);
}
