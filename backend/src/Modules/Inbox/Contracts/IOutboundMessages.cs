namespace Nihasdesk.Modules.Inbox.Contracts;

/// <summary>A team or AI reply waiting to be sent to the customer's channel (ADR 0017).</summary>
public sealed record OutboundMessage(
    Guid MessageId,
    Guid ConversationId,
    string Channel,
    Guid ConnectionId,
    Guid ContactId,
    string Text,
    int Attempts,
    OutboundImage? Image = null,
    OutboundRich? Rich = null);

/// <summary>
/// A photo the team sends (ADR 0031 §6): already checked and re-encoded by the Inbox, read from its
/// storage for this one send. <see cref="OutboundMessage.Text"/> is empty then.
/// </summary>
public sealed record OutboundImage(byte[] Content, string ContentType);

/// <summary>
/// A rich message ready for a channel (ADR 0032 §2): the Inbox has already read titles and prices from
/// the catalog and made one opaque payload per button; the channel only lays it out.
/// </summary>
/// <param name="Kind">One of <see cref="RichKind"/>.</param>
/// <param name="Text">The quick-reply question or the button message text; the carousel's intro (sent first) or null.</param>
/// <param name="Cards">Carousel only.</param>
/// <param name="Buttons">Quick replies or buttons; empty for a carousel.</param>
/// <param name="FallbackText">The numbered text list sent when the channel refuses the rich message.</param>
public sealed record OutboundRich(
    string Kind,
    string? Text,
    IReadOnlyList<OutboundCard> Cards,
    IReadOnlyList<OutboundButton> Buttons,
    string FallbackText);

/// <param name="Title">≤ 80 characters.</param>
/// <param name="Subtitle">≤ 80 characters, e.g. "€89.00 · Nero".</param>
/// <param name="ImageUrl">A signed product image link (ADR 0031 §7), or null while products have no images.</param>
public sealed record OutboundCard(string Title, string? Subtitle, string? ImageUrl, IReadOnlyList<OutboundButton> Buttons);

/// <param name="Label">≤ 20 characters, in the customer's language.</param>
/// <param name="Payload"><c>rm1:&lt;token&gt;</c>: no ids, prices or names.</param>
public sealed record OutboundButton(string Label, string Payload);

/// <summary>A reply that is due, in some workspace. Found across workspaces by a narrow database lookup.</summary>
public sealed record DueDelivery(Guid TenantId, Guid MessageId);

/// <summary>What the channel answered.</summary>
/// <param name="FallbackText">The rich message was refused, so its numbered text list went instead.</param>
public sealed record DeliveryOutcome(string? ExternalId, string? Error, bool Retry, bool FallbackText = false)
{
    public static DeliveryOutcome Sent(string externalId) => new(externalId, null, false);

    /// <summary>Sent, but as the numbered text list instead of the rich message (ADR 0032 §2).</summary>
    public static DeliveryOutcome SentAsText(string externalId) => new(externalId, null, false, FallbackText: true);

    /// <summary>Temporary: network, rate limit, Meta outage. Tried again later.</summary>
    public static DeliveryOutcome Retryable(string error) => new(null, error, true);

    /// <summary>Will never work: blocked by the customer, outside the window, invalid recipient.</summary>
    public static DeliveryOutcome Permanent(string error) => new(null, error, false);
}

/// <summary>How channel modules send replies; the Inbox keeps the delivery state.</summary>
public interface IOutboundMessages
{
    /// <summary>Due replies of all workspaces, oldest first. Needs no tenant; returns IDs only.</summary>
    Task<IReadOnlyList<DueDelivery>> DueAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// In the message's workspace scope: locks the reply (so two senders never send it twice),
    /// re-checks the 24-hour window, renders rich content from the catalog, calls
    /// <paramref name="send"/> and records the outcome. Returns false when the reply was not due or
    /// another sender has it.
    /// </summary>
    Task<bool> DeliverAsync(
        Guid messageId,
        Func<OutboundMessage, CancellationToken, Task<DeliveryOutcome>> send,
        CancellationToken cancellationToken);
}
