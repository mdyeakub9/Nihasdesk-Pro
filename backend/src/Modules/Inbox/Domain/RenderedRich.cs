namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>
/// A rich message as it went to the channel (ADR 0032 §1): names, prices and labels read from the
/// catalog and the i18n labels at send time, with the payload of every button. Stored with the message
/// (<c>rich_rendered</c>), so the team sees exactly what the customer saw (PRD 4.4) and a retry sends
/// the same thing with the same buttons.
/// </summary>
internal sealed record RenderedRich(
    string Kind,
    string? Text,
    IReadOnlyList<RenderedCard> Cards,
    IReadOnlyList<RenderedButton> Buttons,
    string FallbackText);

/// <param name="Price">In cents, from the catalog; null when the card has no single price.</param>
internal sealed record RenderedCard(
    Guid ProductId,
    Guid? VariantId,
    string Title,
    string? Subtitle,
    long? Price,
    string? Currency,
    string? ImageUrl,
    string? MatchLabel,
    IReadOnlyList<RenderedButton> Buttons);

/// <param name="Payload"><c>rm1:&lt;token&gt;</c> of the row in <c>inbox.message_actions</c>.</param>
internal sealed record RenderedButton(string Label, string Action, string Payload);
