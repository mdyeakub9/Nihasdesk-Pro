using System.Text;
using System.Text.Json;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>
/// The tools that send rich content (ADR 0032 §5). The model chooses which products or choices, but
/// only among what tools returned in this turn, checked here like prices (SEC-38); the content itself
/// holds ids, and names and prices are read from the catalog when the message is sent (rule 5).
/// </summary>
internal sealed partial class AgentTools
{
    public const string ShowProductsTool = "show_products";
    public const string ShowChoicesTool = "show_choices";
    public const int IntroMaxLength = 300;

    private static readonly string[] CardButtons = [RichAction.Buy, RichAction.Options, RichAction.Similar];

    private readonly Dictionary<Guid, string> _seenProducts = [];
    private readonly Dictionary<Guid, string> _queryOf = [];
    private readonly StringBuilder _toolText = new();

    /// <summary>The carousel or choices the reply carries; at most one per turn.</summary>
    public RichContent? PendingRich { get; private set; }

    /// <summary>The search behind a carousel and its products, for "Altri prodotti".</summary>
    public (string Query, IReadOnlyList<Guid> ProductIds)? Shown { get; private set; }

    /// <summary>Titles of products tools returned in this turn, for the reply check.</summary>
    public IEnumerable<string> SeenTitles => _seenProducts.Values;

    public string? TitleOf(Guid productId) => _seenProducts.GetValueOrDefault(productId);

    private void Seen(IEnumerable<CatalogProduct> products, string? query)
    {
        foreach (var product in products)
        {
            _seenProducts[product.Id] = product.Title;
            if (query is not null)
            {
                _queryOf.TryAdd(product.Id, query);
            }
        }
    }

    private ToolOutcome ShowProducts(JsonElement args)
    {
        if (PendingRich is not null)
        {
            return Error("Products or choices were already shown in this reply.");
        }

        if (!args.TryGetProperty("product_ids", out var idsJson) || idsJson.ValueKind != JsonValueKind.Array
            || idsJson.GetArrayLength() is < 1 or > ProductCarousel.MaxCards)
        {
            return Error("product_ids: 1 to 10 ids.");
        }

        var ids = new List<Guid>();
        foreach (var item in idsJson.EnumerateArray())
        {
            // Only products a tool returned in this turn: a made-up or remembered id is refused.
            if (item.ValueKind != JsonValueKind.String || !Guid.TryParse(item.GetString(), out var id) || !_seenProducts.ContainsKey(id))
            {
                return Error("Use only product ids that search_products or get_product_options returned in this turn.");
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        var labels = new List<string?>();
        if (args.TryGetProperty("match_labels", out var labelsJson) && labelsJson.ValueKind == JsonValueKind.Array)
        {
            labels.AddRange(labelsJson.EnumerateArray().Select(l => l.GetString() is MatchLabel.Similar or MatchLabel.Likely ? l.GetString() : null));
        }

        var intro = Text(args, "intro", IntroMaxLength);
        var cards = ids.Select((id, i) => new ProductCard(id, null, i < labels.Count ? labels[i] : null, CardButtons)).ToList();
        PendingRich = new RichContent(Carousel: new ProductCarousel(intro, cards));
        Shown = ids.Select(id => _queryOf.GetValueOrDefault(id)).FirstOrDefault(q => q is not null) is { } query ? (query, ids) : null;
        return Result(new
        {
            shown = ids.Count,
            note = "The cards will be sent right after your message, with current prices and buttons. Write only one short line: no product list, no prices.",
        });
    }

    private ToolOutcome ShowChoices(JsonElement args)
    {
        if (PendingRich is not null)
        {
            return Error("Products or choices were already shown in this reply.");
        }

        if (!args.TryGetProperty("options", out var optionsJson) || optionsJson.ValueKind != JsonValueKind.Array
            || optionsJson.GetArrayLength() is < 1 or > QuickReplies.MaxOptions)
        {
            return Error("Send 1 to 13 options.");
        }

        var said = _toolText.ToString();
        var options = new List<RichOption>();
        foreach (var item in optionsJson.EnumerateArray())
        {
            var label = item.ValueKind == JsonValueKind.String ? item.GetString()?.Trim() : null;
            // Values a tool returned in this turn only (e.g. zone names), never the model's own (ADR 0032 §5).
            if (label is not { Length: >= 1 and <= RichOption.LabelMaxLength } || !said.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                return Error("Each option must be a value a tool returned in this turn, at most 20 characters.");
            }

            if (!options.Any(o => string.Equals(o.Label, label, StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(new RichOption(label, RichAction.Choose));
            }
        }

        // The question is the reply itself; ReplyAgent puts the reply's text here before sending.
        PendingRich = new RichContent(QuickReplies: new QuickReplies("?", options));
        return Result(new { shown = options.Count, note = "The choices will be sent as buttons under your message. Ask the question in your message; don't repeat the choices." });
    }

    /// <summary>How many products of this turn the reply names in its text (ADR 0032 §5: at most 2).</summary>
    public int ProductsNamedIn(string reply) =>
        _seenProducts.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(title => title.Length >= 3 && reply.Contains(title, StringComparison.OrdinalIgnoreCase));
}
