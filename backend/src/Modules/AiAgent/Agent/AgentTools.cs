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
internal sealed class AgentTools(ICatalog catalog, IShippingRates shipping, IOrderRequests orders, AiDbContext db)
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

    /// <summary>
    /// The conversation this turn answers, set by the caller from the claimed conversation — never
    /// from the model (SEC-36). Null in the dashboard's test chat, where no order can be requested.
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>Set when this turn created an order request (for the run record).</summary>
    public int? OrderNumber { get; private set; }

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
        Tool("quote_order",
            "The order summary for chosen variants and a delivery zone: unit prices, subtotal, shipping and total, computed by the shop's system. Use it before asking the customer to confirm, and show exactly these amounts.",
            """{"type":"object","properties":{"items":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"product_id":{"type":"string"},"variant_id":{"type":"string","description":"An available variant id from get_product_options"},"quantity":{"type":"integer","minimum":1,"maximum":99}},"required":["product_id","variant_id","quantity"],"additionalProperties":false}},"zone_id":{"type":"string","description":"The id from get_shipping_options"}},"required":["items","zone_id"],"additionalProperties":false}"""),
        Tool("create_order_request",
            "Create an order request for the shop's team to approve, only after the customer confirmed the summary from quote_order. It is NOT a confirmed order: tell the customer the shop will confirm it shortly. Payment is cash on delivery.",
            """{"type":"object","properties":{"items":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"product_id":{"type":"string"},"variant_id":{"type":"string"},"quantity":{"type":"integer","minimum":1,"maximum":99}},"required":["product_id","variant_id","quantity"],"additionalProperties":false}},"zone_id":{"type":"string"},"customer_name":{"type":"string"},"phone":{"type":"string"},"street":{"type":"string","description":"Street and number"},"postal_code":{"type":"string"},"city":{"type":"string"},"province":{"type":"string"},"country":{"type":"string","description":"Two-letter code, e.g. IT"},"notes":{"type":"string"}},"required":["items","zone_id","customer_name","phone","street","postal_code","city","country"],"additionalProperties":false}"""),
        Tool("handoff_to_human",
            "Give the conversation to the shop's team. Use when you don't know the answer, the customer wants a person, has a special request about an order you can't take, complains or asks for a refund, or you are unsure. Don't tell the customer you are 'handing off'; the team will answer.",
            """{"type":"object","properties":{"reason":{"type":"string","enum":["unknown_answer","customer_asked_for_human","complaint","refund","order","special_request","low_confidence","other"]},"question":{"type":"string","description":"For unknown_answer: the customer's question, short, in their words"},"summary":{"type":"string","description":"One sentence for the team"}},"required":["reason"],"additionalProperties":false}"""),
    ];

    /// <summary>The team's AI assistant only reads (ADR 0023): no handoff, no orders, no leads.</summary>
    public static readonly IReadOnlyList<LlmTool> ReadOnlyDefinitions =
        [.. Definitions.Where(t => t.Name is not ("handoff_to_human" or "create_order_request"))];

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
            "quote_order" => await QuoteOrderAsync(args, ct),
            "create_order_request" when !readOnly => await CreateOrderRequestAsync(args, ct),
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
                id = z.Id,
                zone = z.Name,
                areas = z.Areas,
                cost = z.Price == 0 ? "free" : Allowed(z.Price, z.Currency),
                freeShippingFromOrderValue = z.FreeOver is { } free ? Allowed(free, z.Currency) : null,
                deliveryDays = Days(z.MinDays, z.MaxDays),
            }).ToList(),
        });
    }

    /// <summary>The order summary by code (rule 5): its amounts become the only totals the reply may state (SEC-38).</summary>
    private async Task<ToolOutcome> QuoteOrderAsync(JsonElement args, CancellationToken ct)
    {
        if (Lines(args) is not { } lines || Id(args, "zone_id") is not { } zoneId)
        {
            return Error("Send items (product_id, variant_id, quantity 1-99) and the zone_id from get_shipping_options.");
        }

        var quote = await orders.QuoteAsync(lines, zoneId, ct);
        if (!quote.IsSuccess)
        {
            return OrderError(quote.Error.Code, quote.Error.Message);
        }

        var q = quote.Value;
        return Result(new
        {
            lines = q.Lines.Select(l => new
            {
                product = l.Title,
                variant = l.Variant,
                quantity = l.Quantity,
                unitPrice = Allowed(l.UnitPrice, q.Currency),
                lineTotal = Allowed(l.UnitPrice * l.Quantity, q.Currency),
            }).ToList(),
            zone = q.ShippingZoneName,
            shipping = q.ShippingCost == 0 ? "free" : Allowed(q.ShippingCost, q.Currency),
            subtotal = Allowed(q.Subtotal, q.Currency),
            total = Allowed(q.Total, q.Currency),
            payment = "cash on delivery",
            note = "Show this summary with exactly these amounts and ask the customer to confirm before create_order_request.",
        });
    }

    /// <summary>
    /// Creates an order request — never a confirmed order (CLAUDE.md rule 5). The conversation comes
    /// from the turn, the prices from the catalog; validation is done by code (PRD 4.9).
    /// </summary>
    private async Task<ToolOutcome> CreateOrderRequestAsync(JsonElement args, CancellationToken ct)
    {
        if (ConversationId is not { } conversationId)
        {
            return Error("Orders can be requested only in a real conversation, not in the test chat.");
        }

        if (Lines(args) is not { } lines || Id(args, "zone_id") is not { } zoneId)
        {
            return Error("Send items (product_id, variant_id, quantity 1-99) and the zone_id from get_shipping_options.");
        }

        var details = new OrderRequestDetails(
            Text(args, "customer_name", 100) ?? string.Empty,
            Text(args, "phone", 30) ?? string.Empty,
            Text(args, "street", 200) ?? string.Empty,
            Text(args, "postal_code", 12) ?? string.Empty,
            Text(args, "city", 100) ?? string.Empty,
            Text(args, "province", 50),
            Text(args, "country", 2) ?? string.Empty,
            zoneId,
            Text(args, "notes", 500));
        var created = await orders.CreateAsync(conversationId, lines, details, ct);
        if (!created.IsSuccess)
        {
            return OrderError(created.Error.Code, created.Error.Message);
        }

        var order = created.Value;
        OrderNumber = order.Number;
        return Result(new
        {
            ok = true,
            orderNumber = order.Number,
            status = "waiting for the shop's confirmation",
            total = Allowed(order.Total, order.Currency),
            payment = "cash on delivery",
            note = "Tell the customer the request is received and the shop will confirm it shortly. Don't say the order is confirmed.",
        });
    }

    /// <summary>Errors the model can act on: ask again, offer another variant, or let the team handle it.</summary>
    private static ToolOutcome OrderError(string code, string message) => code switch
    {
        "order.invalid" => Error(message),
        "product.not_available" => Error("Not enough stock for one of the items. Check get_product_options and offer what is available."),
        "product.not_found" => Error("A product or variant id is not valid. Use the ids from search_products and get_product_options."),
        "order.zone_not_found" => Error("Unknown zone_id. Use an id from get_shipping_options."),
        "order.pending_exists" => Error("This chat already has an order request waiting for the shop. Tell the customer the team will confirm it shortly; for changes, hand off with reason order."),
        _ => Error("The order couldn't be created. Hand off with reason order."),
    };

    /// <summary>The model's items as catalog IDs and quantities; null when malformed (max 20 lines).</summary>
    private static List<OrderRequestLine>? Lines(JsonElement args)
    {
        if (!args.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is 0 or > 20)
        {
            return null;
        }

        var lines = new List<OrderRequestLine>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || Id(item, "product_id") is not { } productId
                || Id(item, "variant_id") is not { } variantId
                || !item.TryGetProperty("quantity", out var q) || q.ValueKind != JsonValueKind.Number
                || !q.TryGetInt32(out var quantity) || quantity is < 1 or > 99)
            {
                return null;
            }

            lines.Add(new OrderRequestLine(productId, variantId, quantity));
        }

        return lines;
    }

    private static Guid? Id(JsonElement args, string name) =>
        Text(args, name, 40) is { } text && Guid.TryParse(text, out var id) ? id : null;

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
