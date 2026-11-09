namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>
/// A customer message as a channel received it. The workspace is the current tenant of the
/// caller's scope (resolved from the verified channel connection, never from the message itself).
/// </summary>
/// <param name="Channel">One of <c>Contacts.Contracts.ChannelKind</c>.</param>
/// <param name="ExternalContactId">The sender's ID on that channel (PSID, phone, widget visitor ID).</param>
/// <param name="ContactName">Profile name from the channel, if any.</param>
/// <param name="ExternalMessageId">The channel's message ID; repeats are ignored.</param>
/// <param name="ConnectionId">The channel connection (e.g. Facebook Page) that received it; replies go out through it.</param>
/// <param name="Attachments">Media of the message; with them <paramref name="Text"/> may be empty.</param>
/// <param name="ActionPayload">
/// The payload of a tapped button or quick reply, as the channel gave it (ADR 0032 §3). Untrusted: the
/// Inbox looks it up only in the current workspace and conversation; anything else is just <paramref name="Text"/>.
/// </param>
public sealed record InboundMessage(
    string Channel,
    string ExternalContactId,
    string? ContactName,
    string Text,
    string? ExternalMessageId,
    DateTimeOffset SentAt,
    Guid? ConnectionId = null,
    IReadOnlyList<InboundAttachment>? Attachments = null,
    string? ActionPayload = null);

/// <summary>Media as the channel announced it. The Workers job downloads and checks it (ADR 0031 §2).</summary>
/// <param name="Kind">One of <see cref="InboundAttachmentKind"/>.</param>
/// <param name="SourceUrl">Where the channel offers it; null when the channel gave no usable link.</param>
public sealed record InboundAttachment(string Kind, string? SourceUrl);

public static class InboundAttachmentKind
{
    public const string Image = "image";
    public const string Audio = "audio";
    public const string Video = "video";
    public const string File = "file";
    public const string Sticker = "sticker";
}

public sealed record InboundResult(Guid ConversationId, Guid MessageId, bool Duplicate);

/// <summary>How channel modules put customer messages into the inbox (ADR 0016).</summary>
public interface IInboxIngest
{
    Task<InboundResult> ReceiveAsync(InboundMessage message, CancellationToken cancellationToken);
}
