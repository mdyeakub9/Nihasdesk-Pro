using System.Text.Json;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>
/// Checks rich content against the limits of ADR 0032 §1 and turns it into the JSON stored with the
/// message. Content that breaks a limit is refused here, before anything is stored or sent.
/// </summary>
internal static class RichContentJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Serialize(RichContent content)
    {
        Check(content);
        return JsonSerializer.Serialize(content, Json);
    }

    public static RichContent Content(string json) =>
        JsonSerializer.Deserialize<RichContent>(json, Json) ?? throw new InvalidOperationException("Empty rich content.");

    public static string Serialize(RenderedRich rendered) => JsonSerializer.Serialize(rendered, Json);

    public static RenderedRich Rendered(string json) =>
        JsonSerializer.Deserialize<RenderedRich>(json, Json) ?? throw new InvalidOperationException("Empty rendered content.");

    internal static void Check(RichContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var parts = (content.Carousel is null ? 0 : 1) + (content.QuickReplies is null ? 0 : 1) + (content.Buttons is null ? 0 : 1);
        Require(parts == 1, "Exactly one kind of rich content.");

        if (content.Carousel is { } carousel)
        {
            Require(carousel.Cards is { Count: >= 1 and <= ProductCarousel.MaxCards }, "A carousel has 1 to 10 cards.");
            Require(carousel.Intro is null or { Length: <= Message.TextMaxLength }, "The intro is too long.");
            foreach (var card in carousel.Cards)
            {
                Require(card.ProductId != Guid.Empty && card.VariantId != Guid.Empty, "A card needs a product.");
                Require(card.MatchLabel is null or MatchLabel.Likely or MatchLabel.Similar, "Unknown match label.");
                Require(card.Buttons is { Count: <= ProductCard.MaxButtons } && card.Buttons.All(RichAction.CardButtons.Contains)
                    && card.Buttons.Distinct(StringComparer.Ordinal).Count() == card.Buttons.Count, "Unknown or repeated card button.");
            }
        }

        if (content.QuickReplies is { } quick)
        {
            Require(quick.Text is { Length: >= 1 and <= Message.TextMaxLength }, "Quick replies need a question.");
            Require(quick.Options is { Count: >= 1 and <= QuickReplies.MaxOptions }, "1 to 13 quick replies.");
            CheckOptions(quick.Options);
        }

        if (content.Buttons is { } buttons)
        {
            Require(buttons.Text is { Length: >= 1 and <= ButtonMessage.TextMaxLength }, "Buttons need a text of up to 640 characters.");
            Require(buttons.Buttons is { Count: >= 1 and <= ButtonMessage.MaxButtons }, "1 to 3 buttons.");
            CheckOptions(buttons.Buttons);
        }
    }

    private static void CheckOptions(IReadOnlyList<RichOption> options)
    {
        foreach (var option in options)
        {
            Require(option.Label is { Length: >= 1 and <= RichOption.LabelMaxLength }, "A label has 1 to 20 characters.");
            Require(RichAction.All.Contains(option.Action) && option.Action != RichAction.OpenForm, "Unknown action.");
            Require(option.Value is null or { Length: <= RichOption.ValueMaxLength }, "The value is too long.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new ArgumentException(message);
        }
    }
}
