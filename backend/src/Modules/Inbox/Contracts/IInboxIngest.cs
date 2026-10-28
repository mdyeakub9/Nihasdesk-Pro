namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>
/// A customer message as a channel received it. The workspace is the current tenant of the
/// caller's scope (resolved from the verified channel connection, never from the message itself).
/// </summary>
/// <param name="Channel">One of <c>Contacts.Contracts.ChannelKind</c>.</param>
/// <param name="ExternalContactId">The sender's ID on that channel (PSID, phone, widget visitor ID).</param>
/// <param name="ContactName">Profile name from the channel, if any.</param>
/// <param name="ExternalMessageId">The channel's message ID; repeats are ignored.</param>
public sealed record InboundMessage(
    string Channel,
    string ExternalContactId,
    string? ContactName,
    string Text,
    string? ExternalMessageId,
    DateTimeOffset SentAt);

public sealed record InboundResult(Guid ConversationId, Guid MessageId, bool Duplicate);

/// <summary>How channel modules put customer messages into the inbox (ADR 0016).</summary>
public interface IInboxIngest
{
    Task<InboundResult> ReceiveAsync(InboundMessage message, CancellationToken cancellationToken);
}
