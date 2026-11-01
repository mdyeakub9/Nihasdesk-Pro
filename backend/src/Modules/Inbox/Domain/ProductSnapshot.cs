namespace Nihasdesk.Modules.Inbox.Domain;

/// <summary>
/// A catalog product as it was sent to the customer (PRD 4.4, ADR 0024): built on the server from
/// the catalog, never from the client or the model (CLAUDE.md rule 5, SEC-22). Kept with the
/// message so later price changes don't rewrite history.
/// </summary>
internal sealed class ProductSnapshot
{
    public Guid ProductId { get; init; }

    public Guid VariantId { get; init; }

    public string Title { get; init; } = string.Empty;

    /// <summary>Option values, e.g. "Nero / M"; empty for products without options.</summary>
    public string Variant { get; init; } = string.Empty;

    public string? Sku { get; init; }

    /// <summary>In cents.</summary>
    public long Price { get; init; }

    public string Currency { get; init; } = string.Empty;
}
