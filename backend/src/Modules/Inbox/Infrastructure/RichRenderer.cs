using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.Modules.Inbox.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Inbox.Infrastructure;

/// <summary>
/// Turns a message's channel-neutral rich content into what the channel shows (ADR 0032 §1–§3): names,
/// prices and availability come from the catalog now (rule 5, SEC-41), inactive products are dropped,
/// labels are in the customer's language, and every button gets its own opaque token. Rendered once:
/// a retry sends the same message with the same buttons.
/// </summary>
internal sealed class RichRenderer(InboxDbContext db, ICatalog catalog, IContactDirectory contacts, ITenantContext tenant)
{
    public const int TitleMaxLength = 80;
    public const int SubtitleMaxLength = 80;

    public async Task<RenderedRich?> RenderAsync(Message message, Conversation conversation, DateTimeOffset now, CancellationToken ct)
    {
        if (message.RichKind is null || message.RichContent is null)
        {
            return null;
        }

        if (message.RichRendered is { } stored)
        {
            return RichContentJson.Rendered(stored);
        }

        var content = RichContentJson.Content(message.RichContent);
        var contact = (await contacts.GetAsync([conversation.ContactId], ct)).GetValueOrDefault(conversation.ContactId);
        var italian = Italian(contact?.Language);
        var rendered = content switch
        {
            { Carousel: { } carousel } => await CarouselAsync(message, carousel, italian, now, ct),
            { QuickReplies: { } quick } => Options(message, RichKind.QuickReplies, quick.Text, quick.Options, italian, now),
            { Buttons: { } buttons } => Options(message, RichKind.Buttons, buttons.Text, buttons.Buttons, italian, now),
            _ => throw new InvalidOperationException("Unknown rich content."),
        };

        message.Rendered(RichContentJson.Serialize(rendered), fallbackText: false);
        return rendered;
    }

    /// <summary>The conversation's language for labels: Italian unless the contact is known to write in another one.</summary>
    internal static bool Italian(string? language) => language?.StartsWith("it", StringComparison.OrdinalIgnoreCase) ?? true;

    private async Task<RenderedRich> CarouselAsync(Message message, ProductCarousel carousel, bool italian, DateTimeOffset now, CancellationToken ct)
    {
        var cards = new List<RenderedCard>();
        foreach (var card in carousel.Cards)
        {
            // Active products of this workspace only; anything else is left out (ADR 0032 §1).
            var product = await catalog.GetAsync(card.ProductId, ct);
            if (product is null)
            {
                continue;
            }

            var variant = card.VariantId is { } variantId ? product.Variants.SingleOrDefault(v => v.Id == variantId) : null;
            var position = cards.Count * 10;
            var buttons = card.Buttons
                .Select((action, i) => Action(message, position + i, action, RichLabels.Button(action, italian), product.Id, variant?.Id, null, now))
                .ToList();
            var (price, currency, subtitle) = Subtitle(product, variant, card.MatchLabel, italian);
            cards.Add(new RenderedCard(product.Id, variant?.Id, Cut(product.Title, TitleMaxLength), subtitle, price, currency, null, card.MatchLabel, buttons));
        }

        var text = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(carousel.Intro))
        {
            text.Append(carousel.Intro.Trim()).Append('\n');
        }

        if (cards.Count == 0)
        {
            text.Append(RichLabels.NoLongerAvailable(italian));
        }
        else
        {
            for (var i = 0; i < cards.Count; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"{i + 1}. {cards[i].Title}");
                if (cards[i].Subtitle is { } subtitle)
                {
                    text.Append(" — ").Append(subtitle);
                }

                text.Append('\n');
            }

            text.Append(RichLabels.PickProduct(italian));
        }

        return new RenderedRich(RichKind.Carousel, NullIfBlank(carousel.Intro), cards, [], Cut(text.ToString(), Message.TextMaxLength));
    }

    private RenderedRich Options(Message message, string kind, string question, IReadOnlyList<RichOption> options, bool italian, DateTimeOffset now)
    {
        var buttons = options
            .Select((o, i) => Action(message, i, o.Action, o.Label, o.ProductId, o.VariantId, o.Value, now))
            .ToList();
        var text = new StringBuilder(question.Trim()).Append('\n');
        for (var i = 0; i < buttons.Count; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{i + 1}. {buttons[i].Label}\n");
        }

        text.Append(RichLabels.PickOption(italian));
        return new RenderedRich(kind, question.Trim(), [], buttons, Cut(text.ToString(), Message.TextMaxLength));
    }

    private RenderedButton Action(Message message, int position, string action, string label, Guid? productId, Guid? variantId, string? value, DateTimeOffset now)
    {
        var row = MessageAction.Create(tenant.TenantId, message.ConversationId, message.Id, position, action, label, productId, variantId, value, now);
        db.MessageActions.Add(row);
        return new RenderedButton(row.Label, action, row.Payload);
    }

    /// <summary>"€89.00 · Nero", "da €25.00", "€89.00 · Simile", "Esaurito".</summary>
    private static (long? Price, string? Currency, string? Subtitle) Subtitle(CatalogProduct product, CatalogVariant? variant, string? matchLabel, bool italian)
    {
        var parts = new List<string>();
        long? price = null;
        string? currency = null;
        var available = variant is not null ? variant.Available : product.Variants.Any(v => v.Available);
        if (variant is not null)
        {
            price = variant.Price;
            currency = variant.Currency;
            parts.Add(Money(variant.Price, variant.Currency));
            var options = string.Join(" / ", variant.OptionValues.Where(v => !string.IsNullOrWhiteSpace(v)));
            if (options.Length > 0)
            {
                parts.Add(options);
            }
        }
        else if (product.Variants.Where(v => v.Available).ToList() is { Count: > 0 } inStock)
        {
            var cheapest = inStock.MinBy(v => v.Price)!;
            var single = inStock.All(v => v.Price == cheapest.Price);
            price = single ? cheapest.Price : null;
            currency = cheapest.Currency;
            parts.Add(single ? Money(cheapest.Price, cheapest.Currency) : $"{RichLabels.From(italian)} {Money(cheapest.Price, cheapest.Currency)}");
        }

        if (matchLabel == MatchLabel.Similar)
        {
            parts.Add(RichLabels.Similar(italian));
        }

        if (!available)
        {
            parts.Add(RichLabels.SoldOut(italian));
        }

        return (price, currency, parts.Count == 0 ? null : Cut(string.Join(" · ", parts), SubtitleMaxLength));
    }

    /// <summary>8900 EUR → "€89.00"; other currencies "89.00 USD".</summary>
    internal static string Money(long cents, string currency)
    {
        var amount = (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return currency == "EUR" ? $"€{amount}" : $"{amount} {currency}";
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}

/// <summary>Taps whose token was found in the current workspace and conversation (ADR 0032 §3).</summary>
internal static class MessageActionLookup
{
    public static Task<MessageAction?> FindAsync(InboxDbContext db, string? payload, Guid conversationId, CancellationToken ct) =>
        MessageAction.TokenOf(payload) is { } token
            ? db.MessageActions.SingleOrDefaultAsync(a => a.Token == token && a.ConversationId == conversationId, ct)
            : Task.FromResult<MessageAction?>(null);
}
