using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Shipping.Contracts;

namespace Nihasdesk.Modules.AiAgent.Agent;

/// <summary>Why the AI hands a conversation to the team (PRD 4.8).</summary>
internal static class HandoffReasons
{
    public const string UnknownAnswer = "unknown_answer";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        UnknownAnswer, "customer_asked_for_human", "complaint", "refund", "order", "special_request", "low_confidence", "other",
    };
}

/// <summary>The result of one tool call: JSON for the model, or a handoff that ends the turn.</summary>
internal sealed record ToolOutcome(string Json, string? HandoffReason = null, string? Question = null);

/// <summary>Topics of the shop's written policies (ADR 0020).</summary>
internal static class KnowledgeTopics
{
    public const string Shipping = "shipping";
    public const string Returns = "returns";
    public const string Payment = "payment";
    public const string Contact = "contact";
    public const string Other = "other";
}

/// <summary>What an answer was based on, as stored in <c>ai.runs.sources</c> (no customer text).</summary>
internal static class KnowledgeSources
{
    public const string Shipping = "shipping";
    public const string PolicyPrefix = "policy:";
    public const string AnswerPrefix = "answer:";
}

/// <summary>
/// The AI's tools (ADR 0019, 0020, SEC-36). Arguments are the model's JSON: parsed and checked
/// here. The workspace comes from the scope, never from the model. One instance per AI turn: it
/// also remembers which prices tools returned, for the reply check (SEC-38), and which knowledge
/// the answer used.
/// </summary>
internal sealed class AgentTools(ICatalog catalog, IShippingRates shipping, AiDbContext db)
{
    // Tool results go to the model, never into HTML: keep "+39…" and "€" readable instead of +.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HashSet<long> _prices = [];
    private readonly List<string> _sources = [];

    /// <summary>Prices (in cents) that tools returned in this turn; the only ones a reply may mention.</summary>
    public IReadOnlySet<long> AllowedPrices => _prices;

    /// <summary>Knowledge returned in this turn, see <see cref="KnowledgeSources"/>.</summary>
    public IReadOnlyList<string> Sources => _sources;

    public static readonly IReadOnlyList<LlmTool> Definitions =
    [
        Tool("search_products",
            "Find products in this shop's catalog by words the customer used (name, type, colour, brand…). Returns prices, options and availability. Always use it before mentioning any product or price.",
            """{"type":"object","properties":{"query":{"type":"string","description":"Search words, e.g. 'black leather bag'"}},"required":["query"],"additionalProperties":false}"""),
        Tool("get_product_options",
            "The variants of one product that can be ordered now (size, colour…), with prices. Use before offering options.",
            """{"type":"object","properties":{"product_id":{"type":"string","description":"The id from search_products"}},"required":["product_id"],"additionalProperties":false}"""),
        Tool("get_shipping_options",
            "Where the shop delivers, the shipping cost per zone, free-shipping thresholds and delivery times. Use it for any question about shipping costs or delivery times.",
            """{"type":"object","properties":{},"additionalProperties":false}"""),
        Tool("search_knowledge",
            "The shop's own rules and answers: returns, payment, contact details and opening hours, and answers the team wrote for customer questions. Use it for any question that is not about a product or shipping cost.",
            """{"type":"object","properties":{"query":{"type":"string","description":"The customer's question in a few words"},"topic":{"type":"string","enum":["shipping","returns","payment","contact","other"],"description":"What the question is about"}},"required":["query","topic"],"additionalProperties":false}"""),
        Tool("handoff_to_human",
            "Give the conversation to the shop's team. Use when you don't know the answer, the customer wants a person, wants to order, complains or asks for a refund, or you are unsure. Don't tell the customer you are 'handing off'; the team will answer.",
            """{"type":"object","properties":{"reason":{"type":"string","enum":["unknown_answer","customer_asked_for_human","complaint","refund","order","special_request","low_confidence","other"]},"question":{"type":"string","description":"For unknown_answer: the customer's question, short, in their words"},"summary":{"type":"string","description":"One sentence for the team"}},"required":["reason"],"additionalProperties":false}"""),
    ];

    /// <summary>The team's AI assistant only reads (ADR 0023): no handoff, no orders, no leads.</summary>
    public static readonly IReadOnlyList<LlmTool> ReadOnlyDefinitions =
        [.. Definitions.Where(t => t.Name != "handoff_to_human")];

    /// <summary><paramref name="readOnly"/>: tools that change something are refused, whatever the model asks.</summary>
    public async Task<ToolOutcome> RunAsync(LlmToolCall call, CancellationToken ct, bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(call);
        JsonElement args;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
            args = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return Error("Arguments must be JSON.");
        }

        if (args.ValueKind != JsonValueKind.Object)
        {
            return Error("Arguments must be an object.");
        }

        return call.Name switch
        {
            "search_products" => await SearchProductsAsync(args, ct),
            "get_product_options" => await ProductOptionsAsync(args, ct),
            "get_shipping_options" => await ShippingOptionsAsync(ct),
            "search_knowledge" => await SearchKnowledgeAsync(args, ct),
            "handoff_to_human" when !readOnly => Handoff(args),
            _ => Error("Unknown tool."),
        };
    }

    private async Task<ToolOutcome> SearchProductsAsync(JsonElement args, CancellationToken ct)
    {
        if (Text(args, "query", 200) is not { } query)
        {
            return Error("query is required (max 200 characters).");
        }

        var products = await catalog.SearchAsync(query, 5, ct);
        return Result(new
        {
            products = products.Select(p => new
            {
                id = p.Id,
                title = p.Title,
                description = Shorten(p.Description, 300),
                category = p.Category,
                brand = p.Vendor,
                options = p.OptionNames,
                variants = p.Variants.Select(Variant).ToList(),
            }).ToList(),
            note = products.Count == 0 ? "No matching products. Don't invent any." : null,
        });
    }

    private async Task<ToolOutcome> ProductOptionsAsync(JsonElement args, CancellationToken ct)
    {
        if (Text(args, "product_id", 40) is not { } text || !Guid.TryParse(text, out var productId))
        {
            return Error("product_id must be an id from search_products.");
        }

        // A product of another workspace (or a made-up id) is simply not found.
        var product = await catalog.GetAsync(productId, ct);
        if (product is null)
        {
            return Error("Product not found.");
        }

        return Result(new
        {
            id = product.Id,
            title = product.Title,
            options = product.OptionNames,
            available = product.Variants.Where(v => v.Available).Select(Variant).ToList(),
            soldOut = product.Variants.Where(v => !v.Available).Select(v => string.Join(" / ", v.OptionValues)).ToList(),
        });
    }

    private async Task<ToolOutcome> SearchKnowledgeAsync(JsonElement args, CancellationToken ct)
    {
        if (Text(args, "query", 300) is not { } query)
        {
            return Error("query is required (max 300 characters).");
        }

        var topic = Text(args, "topic", 20) ?? KnowledgeTopics.Other;
        var policy = await PolicyAsync(topic, ct);

        // Keyword scoring in memory (ADR 0020): a shop has at most a few hundred answers.
        // Embeddings come with document import.
        var stems = Stems(query);
        var candidates = stems.Count == 0
            ? []
            : await db.Answers
                .AsNoTracking()
                .Where(a => a.Active)
                .OrderByDescending(a => a.UpdatedAt)
                .Take(1000)
                .ToListAsync(ct);
        var best = candidates
            .Select(a => (Answer: a, Score: stems.Count(s => a.Question.Contains(s, StringComparison.OrdinalIgnoreCase)) * 2
                + stems.Count(s => a.Answer.Contains(s, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Answer)
            .ToList();
        foreach (var answer in best)
        {
            Used(KnowledgeSources.AnswerPrefix + answer.Id, answer.Answer);
        }

        return Result(new
        {
            policy,
            answers = best.Select(a => new { question = a.Question, answer = a.Answer }).ToList(),
            note = policy is null && best.Count == 0 ? "The shop hasn't written this down. Hand off with reason unknown_answer." : null,
        });
    }

    /// <summary>The written policy for a topic, or null when the shop left it empty.</summary>
    private async Task<object?> PolicyAsync(string topic, CancellationToken ct)
    {
        var p = await db.Policies.AsNoTracking().SingleOrDefaultAsync(ct);
        switch (topic)
        {
            case KnowledgeTopics.Returns when p is not null && (p.ReturnDays is not null || p.ReturnPolicy is not null):
                Used(KnowledgeSources.PolicyPrefix + topic, p.ReturnPolicy);
                return new { returnWithinDaysOfDelivery = p.ReturnDays, rules = p.ReturnPolicy };
            case KnowledgeTopics.Payment:
                // Cash on delivery is the only method in Phase 1 (ADR 0004), whether or not the shop wrote notes.
                Used(KnowledgeSources.PolicyPrefix + topic, p?.PaymentNotes);
                return new { methods = new[] { "cash on delivery" }, notes = p?.PaymentNotes };
            case KnowledgeTopics.Contact when p is not null && (p.ContactPhone ?? p.ContactEmail ?? p.ContactAddress ?? p.OpeningHours) is not null:
                Used(KnowledgeSources.PolicyPrefix + topic, null);
                return new { phone = p.ContactPhone, email = p.ContactEmail, address = p.ContactAddress, openingHours = p.OpeningHours };
            case KnowledgeTopics.Shipping when p?.ShippingNotes is not null:
                Used(KnowledgeSources.PolicyPrefix + topic, p.ShippingNotes);
                return new { notes = p.ShippingNotes, costs = "Use get_shipping_options for costs and delivery times." };
            default:
                return null;
        }
    }

    private async Task<ToolOutcome> ShippingOptionsAsync(CancellationToken ct)
    {
        var zones = await shipping.ListAsync(ct);
        if (zones.Count == 0)
        {
            return Result(new { zones = Array.Empty<object>(), note = "The shop hasn't set shipping costs. Hand off with reason unknown_answer." });
        }

        Used(KnowledgeSources.Shipping, null);
        return Result(new
        {
            zones = zones.Select(z => new
            {
                zone = z.Name,
                areas = z.Areas,
                cost = z.Price == 0 ? "free" : Allowed(z.Price, z.Currency),
                freeShippingFromOrderValue = z.FreeOver is { } free ? Allowed(free, z.Currency) : null,
                deliveryDays = Days(z.MinDays, z.MaxDays),
            }).ToList(),
        });
    }

    /// <summary>
    /// Records a source; amounts in the shop's own text (e.g. "return shipping €5") may then be
    /// quoted, because the owner wrote them, not the model.
    /// </summary>
    private void Used(string source, string? text)
    {
        if (!_sources.Contains(source))
        {
            _sources.Add(source);
        }

        foreach (var cents in Guardrails.Amounts(text))
        {
            _prices.Add(cents);
        }
    }

    /// <summary>
    /// Word starts of 3+ letters/digits ("spedizione", "spedite" → "spedi"), so close forms match.
    /// Letters and digits only (SEC-01).
    /// </summary>
    internal static List<string> Stems(string query) =>
        System.Text.RegularExpressions.Regex.Matches(query, @"[\p{L}\p{N}]{3,}")
            .Select(m => m.Value.ToLowerInvariant())
            .Select(w => w.Length > 5 ? w[..5] : w)
            .Distinct()
            .Take(8)
            .ToList();

    private static string? Days(int? min, int? max) => (min, max) switch
    {
        (null, null) => null,
        ({ } a, { } b) when a != b => $"{a}-{b} days",
        _ => $"{min ?? max} days",
    };

    private string Allowed(long cents, string currency)
    {
        _prices.Add(cents);
        return Money(cents, currency);
    }

    private static ToolOutcome Handoff(JsonElement args)
    {
        var reason = Text(args, "reason", 40);
        if (reason is null || !HandoffReasons.All.Contains(reason))
        {
            return Error("reason must be one of the listed values.");
        }

        return new ToolOutcome("""{"ok":true}""", reason, Text(args, "question", 1000) ?? Text(args, "summary", 300));
    }

    private object Variant(CatalogVariant v)
    {
        _prices.Add(v.Price);
        if (v.CompareAtPrice is { } compare)
        {
            _prices.Add(compare);
        }

        return new
        {
            id = v.Id,
            options = v.OptionValues,
            price = Money(v.Price, v.Currency),
            priceBeforeSale = v.CompareAtPrice is { } before ? Money(before, v.Currency) : null,
            available = v.Available,
            inStock = v.Stock,
        };
    }

    private static string Money(long cents, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{cents / 100}.{cents % 100:00} {currency}");

    private static string? Text(JsonElement args, string name, int max) =>
        args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text && text.Length <= max
            ? text
            : null;

    private static string? Shorten(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max] + "…";

    private static ToolOutcome Result(object value) => new(JsonSerializer.Serialize(value, Json));

    private static ToolOutcome Error(string message) => new(JsonSerializer.Serialize(new { error = message }, Json));

    private static LlmTool Tool(string name, string description, string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return new LlmTool(name, description, document.RootElement.Clone());
    }
}
