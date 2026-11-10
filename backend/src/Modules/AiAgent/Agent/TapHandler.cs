using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>What code answers to a tap: messages to send, or a handoff. Null from the handler means "the model answers".</summary>
internal sealed record TapAnswer(IReadOnlyList<AiReply> Replies, string Action, string Result, string? HandoffReason = null);

/// <summary>
/// Answers button taps with code, before and instead of the model (ADR 0032 §4): instant, free, and
/// with prices, stock and options read from the catalog only (rule 5). What the tap acts on comes from
/// the Inbox's own action rows, never from the payload (SEC-36). Free text after a tap goes to the model.
/// </summary>
internal sealed class TapHandler(ICatalog catalog, AiDbContext db, ITenantContext tenant, TimeProvider time)
{
    public const int SearchLimit = 20;

    /// <summary>Null when the last message isn't a tap code answers; the model (or the team) does then.</summary>
    public async Task<TapAnswer?> AnswerAsync(AiConversation conversation, bool italian, CancellationToken ct)
    {
        var last = conversation.Messages.Count > 0 ? conversation.Messages[^1] : null;
        if (last is not { Author: "customer", Tap: { } tap }
            || (conversation.CodeOnly && !RichAction.CodeAnswersForTeam.Contains(tap.Action)))
        {
            return null;
        }

        var draft = await DraftAsync(conversation, ct);
        var words = new Words(italian);
        return tap.Action switch
        {
            RichAction.Buy or RichAction.Options => await StartAsync(draft, tap, words, ct),
            RichAction.PickVariant => await PickAsync(draft, tap, words, ct),
            RichAction.Similar => await SimilarAsync(draft, tap, words, ct),
            RichAction.MoreProducts => await MoreAsync(draft, tap, words, ct),
            RichAction.TalkToHuman => new TapAnswer([], tap.Action, "handoff", HandoffReason: "customer_asked_for_human"),
            // choose, edit_summary, confirm_summary, open_form: the customer's words, for the model.
            _ => null,
        };
    }

    /// <summary>The conversation's draft; an old one (7 days) starts over.</summary>
    public async Task<OrderDraft> DraftAsync(AiConversation conversation, CancellationToken ct)
    {
        var draft = await db.Drafts.SingleOrDefaultAsync(d => d.ConversationId == conversation.ConversationId, ct);
        if (draft is not null && draft.IsExpired(time.GetUtcNow()))
        {
            db.Drafts.Remove(draft);
            await db.SaveChangesAsync(ct);
            draft = null;
        }

        if (draft is null)
        {
            draft = OrderDraft.Start(tenant.TenantId, conversation.ConversationId, conversation.ContactId);
            db.Drafts.Add(draft);
        }

        return draft;
    }

    /// <summary>
    /// The items chosen with buttons, for the model when the customer writes next (it quotes and
    /// collects the details, PRD 4.9 step 3). Ids from the shop system; titles and options from the catalog.
    /// </summary>
    public async Task<string?> OrderSoFarAsync(OrderDraft draft, CancellationToken ct)
    {
        if (draft.Lines.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder("Items the customer already chose with the buttons (from the shop system; use these ids for quote_order and create_order_request, quantities may still change):\n");
        foreach (var line in draft.Lines)
        {
            var product = await catalog.GetAsync(line.ProductId, ct);
            var variant = product?.Variants.SingleOrDefault(v => v.Id == line.VariantId);
            var name = product is null ? "(no longer available)" : $"{product.Title}{Options(variant)}";
            text.Append(CultureInfo.InvariantCulture, $"- product_id {line.ProductId}, variant_id {line.VariantId}, \"{name}\", quantity {line.Quantity}\n");
        }

        return text.ToString();
    }

    private async Task<TapAnswer> StartAsync(OrderDraft draft, AiTap tap, Words words, CancellationToken ct)
    {
        if (await ProductAsync(tap, ct) is not { } product)
        {
            return Gone(tap, words);
        }

        // "Lo voglio" on a card for one variant: that variant, only to be confirmed (PRD 4.9 step 2).
        var preselected = tap.Action == RichAction.Buy ? product.Variants.SingleOrDefault(v => v.Id == tap.VariantId && v.Available) : null;
        draft.Choose(product.Id, preselected?.Id);
        if (preselected is not null)
        {
            var question = words.ThisOne($"{product.Title}{Options(preselected)}", Money(preselected));
            return Choices(tap, question,
            [
                new RichOption(words.YesThisOne, RichAction.PickVariant, product.Id, preselected.Id),
                new RichOption(words.OtherOptions, RichAction.Options, product.Id),
            ]);
        }

        return Next(draft, product, tap, words);
    }

    private async Task<TapAnswer> PickAsync(OrderDraft draft, AiTap tap, Words words, CancellationToken ct)
    {
        if (await ProductAsync(tap, ct) is not { } product)
        {
            return Gone(tap, words);
        }

        if (tap.VariantId is { } variantId)
        {
            var variant = product.Variants.SingleOrDefault(v => v.Id == variantId);
            if (variant is { Available: true })
            {
                return Complete(draft, product, variant, tap, words);
            }

            draft.Choose(product.Id, null);
            return Next(draft, product, tap, words);
        }

        // Taps arrive one option at a time; an old tap (another product, an earlier question) starts over.
        if (draft.ProductId != product.Id || tap.Value is null || !Fits(product, draft.ChosenValues, tap.Value))
        {
            draft.Choose(product.Id, null);
        }

        if (tap.Value is not null && Fits(product, draft.ChosenValues, tap.Value))
        {
            draft.PickValue(tap.Value);
        }

        return Next(draft, product, tap, words);
    }

    /// <summary>The next option as quick replies (one dimension at a time, in-stock only), or the finished item.</summary>
    private static TapAnswer Next(OrderDraft draft, CatalogProduct product, AiTap tap, Words words)
    {
        var available = Matching(product, draft.ChosenValues);
        if (available.Count == 0)
        {
            if (draft.ChosenValues.Count > 0 && Matching(product, []).Count > 0)
            {
                draft.Choose(product.Id, null);
                return Next(draft, product, tap, words);
            }

            var text = words.SoldOut(product.Title);
            return new TapAnswer(
                [new AiReply(text, new RichContent(QuickReplies: new QuickReplies(text, [new RichOption(words.Similar, RichAction.Similar, product.Id)])))],
                tap.Action, "sold_out");
        }

        var dimensions = Math.Max(product.OptionNames.Count, available.Max(v => v.OptionValues.Count));
        while (draft.ChosenValues.Count < dimensions)
        {
            var index = draft.ChosenValues.Count;
            var values = available.Select(v => index < v.OptionValues.Count ? v.OptionValues[index] : string.Empty).Distinct(StringComparer.Ordinal).ToList();
            if (values.Count > 1)
            {
                if (values.Count > QuickReplies.MaxOptions || values.Any(v => v.Length is 0 or > RichOption.LabelMaxLength))
                {
                    // Too many or too long for buttons: the customer writes it, the model checks it.
                    return new TapAnswer([new AiReply(words.WhichOption(OptionName(product, index, words), product.Title))], tap.Action, "ask_in_text");
                }

                var question = words.WhichOption(OptionName(product, index, words), product.Title);
                return Choices(tap, question, [.. values.Select(v => new RichOption(v, RichAction.PickVariant, product.Id, null, v))]);
            }

            // Only one value in stock: no need to ask.
            draft.PickValue(values[0]);
            available = Matching(product, draft.ChosenValues);
        }

        return Complete(draft, product, available[0], tap, words);
    }

    private static TapAnswer Complete(OrderDraft draft, CatalogProduct product, CatalogVariant variant, AiTap tap, Words words)
    {
        draft.AddLine(product.Id, variant.Id);
        return new TapAnswer([new AiReply(words.Chosen($"{product.Title}{Options(variant)}", Money(variant)))], tap.Action, "item_added");
    }

    private async Task<TapAnswer> SimilarAsync(OrderDraft draft, AiTap tap, Words words, CancellationToken ct)
    {
        var product = await ProductAsync(tap, ct);
        var found = new List<CatalogProduct>();
        string? query = null;
        foreach (var candidate in new[] { product?.Category, product?.Title }.Where(q => !string.IsNullOrWhiteSpace(q)))
        {
            query = candidate!;
            found = [.. (await catalog.SearchAsync(query, SearchLimit, ct)).Where(p => p.Id != product!.Id).Take(ProductCarousel.MaxCards)];
            if (found.Count > 0)
            {
                break;
            }
        }

        if (found.Count == 0)
        {
            var text = words.NoSimilar;
            return new TapAnswer(
                [new AiReply(text, new RichContent(QuickReplies: new QuickReplies(text, [new RichOption(words.TalkToUs, RichAction.TalkToHuman)])))],
                tap.Action, "none_found");
        }

        draft.Shown(query!, found.Select(p => p.Id), nextPage: false);
        return Carousel(tap, words.SimilarIntro, found, MatchLabel.Similar);
    }

    private async Task<TapAnswer?> MoreAsync(OrderDraft draft, AiTap tap, Words words, CancellationToken ct)
    {
        if (draft.LastQuery is not { } query)
        {
            return null;
        }

        var shown = draft.ShownProductIds.ToHashSet();
        var found = (await catalog.SearchAsync(query, SearchLimit, ct)).Where(p => !shown.Contains(p.Id)).Take(ProductCarousel.MaxCards).ToList();
        if (found.Count == 0)
        {
            return new TapAnswer([new AiReply(words.NoMore)], tap.Action, "none_found");
        }

        draft.Shown(query, found.Select(p => p.Id), nextPage: true);
        return Carousel(tap, words.MoreIntro, found, null);
    }

    private static TapAnswer Carousel(AiTap tap, string intro, IReadOnlyList<CatalogProduct> products, string? matchLabel)
    {
        var cards = products.Select(p => new ProductCard(p.Id, null, matchLabel, [RichAction.Buy, RichAction.Options, RichAction.Similar])).ToList();
        return new TapAnswer(
            [new AiReply(CarouselText(intro, products.Select(p => p.Title)), new RichContent(Carousel: new ProductCarousel(intro, cards)))],
            tap.Action,
            string.Create(CultureInfo.InvariantCulture, $"carousel:{cards.Count}"));
    }

    /// <summary>What the history and the team's list read for a carousel: the intro and the titles, no prices.</summary>
    public static string CarouselText(string? intro, IEnumerable<string> titles)
    {
        var list = "🛍️ " + string.Join(" · ", titles);
        return string.IsNullOrWhiteSpace(intro) ? list : $"{intro}\n{list}";
    }

    private static TapAnswer Choices(AiTap tap, string question, IReadOnlyList<RichOption> options) =>
        new([new AiReply(question, new RichContent(QuickReplies: new QuickReplies(question, options)))], tap.Action,
            string.Create(CultureInfo.InvariantCulture, $"quick_replies:{options.Count}"));

    private static TapAnswer Gone(AiTap tap, Words words) => new([new AiReply(words.Gone)], tap.Action, "product_gone");

    private async Task<CatalogProduct?> ProductAsync(AiTap tap, CancellationToken ct) =>
        tap.ProductId is { } id ? await catalog.GetAsync(id, ct) : null;

    private static List<CatalogVariant> Matching(CatalogProduct product, IReadOnlyList<string> chosen) =>
        [.. product.Variants.Where(v => v.Available
            && chosen.Select((value, i) => i < v.OptionValues.Count && string.Equals(v.OptionValues[i], value, StringComparison.Ordinal)).All(ok => ok))];

    /// <summary>Is <paramref name="value"/> an in-stock choice for the next option after <paramref name="chosen"/>?</summary>
    private static bool Fits(CatalogProduct product, List<string> chosen, string value) =>
        Matching(product, chosen).Any(v => chosen.Count < v.OptionValues.Count && string.Equals(v.OptionValues[chosen.Count], value, StringComparison.Ordinal));

    private static string OptionName(CatalogProduct product, int index, Words words) =>
        index < product.OptionNames.Count && !string.IsNullOrWhiteSpace(product.OptionNames[index]) ? product.OptionNames[index] : words.Option;

    private static string Options(CatalogVariant? variant)
    {
        var values = variant is null ? string.Empty : string.Join(" / ", variant.OptionValues.Where(v => !string.IsNullOrWhiteSpace(v)));
        return values.Length == 0 ? string.Empty : $" — {values}";
    }

    /// <summary>8900 EUR → "€89.00", as the cards show it; from the catalog only.</summary>
    private static string Money(CatalogVariant variant)
    {
        var amount = (variant.Price / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return variant.Currency == "EUR" ? $"€{amount}" : $"{amount} {variant.Currency}";
    }

    /// <summary>What tap code says, in Italian or English. Labels fit Messenger's 20 characters.</summary>
    private sealed record Words(bool Italian)
    {
        public string YesThisOne => Italian ? "✅ Sì, questa" : "✅ Yes, this one";

        public string OtherOptions => Italian ? "📏 Altre opzioni" : "📏 Other options";

        public string Similar => Italian ? "🔍 Simili" : "🔍 Similar";

        public string TalkToUs => Italian ? "Parla con noi" : "Talk to a person";

        public string Option => Italian ? "opzione" : "option";

        public string Gone => Italian
            ? "Questo prodotto non è più disponibile. Vuoi che ti mostri qualcosa di simile?"
            : "This product is no longer available. Shall I show you something similar?";

        public string NoSimilar => Italian
            ? "Non ho trovato prodotti simili. Vuoi parlare con qualcuno del negozio?"
            : "I couldn't find similar products. Would you like to talk to someone from the shop?";

        public string NoMore => Italian ? "Non ci sono altri prodotti per questa ricerca." : "There are no more products for this search.";

        public string SimilarIntro => Italian ? "Ecco alcuni prodotti simili 👇" : "Here are some similar products 👇";

        public string MoreIntro => Italian ? "Ecco altri prodotti 👇" : "Here are more products 👇";

        public string ThisOne(string item, string price) => Italian ? $"{item}, {price}. Va bene questa?" : $"{item}, {price}. Is this the one?";

        public string WhichOption(string option, string title) =>
            Italian ? $"{title}: quale {option.ToLowerInvariant()} preferisci?" : $"{title}: which {option.ToLowerInvariant()} would you like?";

        public string SoldOut(string title) =>
            Italian ? $"Mi dispiace, {title} al momento è esaurito." : $"Sorry, {title} is sold out at the moment.";

        public string Chosen(string item, string price) => Italian
            ? $"Perfetto: {item}, {price}. Per la consegna mi servono nome e cognome, telefono e indirizzo (via e numero, CAP, città, provincia)."
            : $"Great: {item}, {price}. For delivery I need your full name, phone number and address (street and number, postal code, city, province).";
    }
}
