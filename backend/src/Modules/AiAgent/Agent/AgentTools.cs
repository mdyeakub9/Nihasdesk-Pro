using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Commerce.Contracts;

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

/// <summary>
/// The AI's tools (ADR 0019, SEC-36). Arguments are the model's JSON: parsed and checked here.
/// The workspace comes from the scope, never from the model. One instance per AI turn: it also
/// remembers which prices tools returned, for the reply check (SEC-38).
/// </summary>
internal sealed class AgentTools(ICatalog catalog, AiDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HashSet<long> _prices = [];

    /// <summary>Prices (in cents) that tools returned in this turn; the only ones a reply may mention.</summary>
    public IReadOnlySet<long> AllowedPrices => _prices;

    public static readonly IReadOnlyList<LlmTool> Definitions =
    [
        Tool("search_products",
            "Find products in this shop's catalog by words the customer used (name, type, colour, brand…). Returns prices, options and availability. Always use it before mentioning any product or price.",
            """{"type":"object","properties":{"query":{"type":"string","description":"Search words, e.g. 'black leather bag'"}},"required":["query"],"additionalProperties":false}"""),
        Tool("get_product_options",
            "The variants of one product that can be ordered now (size, colour…), with prices. Use before offering options.",
            """{"type":"object","properties":{"product_id":{"type":"string","description":"The id from search_products"}},"required":["product_id"],"additionalProperties":false}"""),
        Tool("search_knowledge",
            "Answers the shop's team has written for customer questions (shipping, returns, opening hours, payment…). Use it for any question that is not about a product.",
            """{"type":"object","properties":{"query":{"type":"string","description":"The customer's question in a few words"}},"required":["query"],"additionalProperties":false}"""),
        Tool("handoff_to_human",
            "Give the conversation to the shop's team. Use when you don't know the answer, the customer wants a person, wants to order, complains or asks for a refund, or you are unsure. Don't tell the customer you are 'handing off'; the team will answer.",
            """{"type":"object","properties":{"reason":{"type":"string","enum":["unknown_answer","customer_asked_for_human","complaint","refund","order","special_request","low_confidence","other"]},"question":{"type":"string","description":"For unknown_answer: the customer's question, short, in their words"},"summary":{"type":"string","description":"One sentence for the team"}},"required":["reason"],"additionalProperties":false}"""),
    ];

    public async Task<ToolOutcome> RunAsync(LlmToolCall call, CancellationToken ct)
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
            "search_knowledge" => await SearchKnowledgeAsync(args, ct),
            "handoff_to_human" => Handoff(args),
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

        // Letters and digits only, so nothing in a word acts as an ILIKE wildcard (SEC-01).
        var words = System.Text.RegularExpressions.Regex.Matches(query, @"[\p{L}\p{N}]{3,}")
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .Take(8)
            .ToList();
        if (words.Count == 0)
        {
            return Result(new { answers = Array.Empty<object>() });
        }

        // Keyword scoring in memory for now (ADR 0019): a shop has at most a few hundred taught answers.
        // Embeddings come with the knowledge base.
        var candidates = await db.Answers
            .AsNoTracking()
            .Where(a => a.Active)
            .OrderByDescending(a => a.UpdatedAt)
            .Take(1000)
            .ToListAsync(ct);
        var best = candidates
            .Select(a => (Answer: a, Score: words.Count(w => a.Question.Contains(w, StringComparison.OrdinalIgnoreCase)) * 2
                + words.Count(w => a.Answer.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => new { question = x.Answer.Question, answer = x.Answer.Answer })
            .ToList();

        return Result(new
        {
            answers = best,
            note = best.Count == 0 ? "The team hasn't answered this yet. Hand off with reason unknown_answer." : null,
        });
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
