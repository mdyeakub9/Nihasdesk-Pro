namespace Nihasdesk.Modules.Commerce.Contracts;

/// <summary>
/// A variant as the AI and other modules may see it: price and availability, never the cost.
/// <paramref name="ImageId"/> is its own saved picture, if it has one (link it with <see cref="IProductImageLinks"/>).
/// </summary>
public sealed record CatalogVariant(
    Guid Id,
    string? Sku,
    IReadOnlyList<string> OptionValues,
    long Price,
    long? CompareAtPrice,
    string Currency,
    bool Available,
    int? Stock,
    Guid? ImageId = null);

/// <param name="MainImageId">The first saved picture of the product, or null.</param>
public sealed record CatalogProduct(
    Guid Id,
    string Title,
    string? Description,
    string? Category,
    string? Vendor,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> OptionNames,
    IReadOnlyList<CatalogVariant> Variants,
    Guid? MainImageId = null);

/// <summary>
/// The current workspace's catalog, <b>active</b> products only (ADR 0018). The one source of prices
/// and stock for the AI (CLAUDE.md rule 5): tools read it; the model never invents them.
/// </summary>
public interface ICatalog
{
    /// <summary>
    /// Products matching any word of the query in title, SKU, options, category, vendor, tags or
    /// description, best match first; at most <paramref name="limit"/> (≤ 20).
    /// </summary>
    Task<IReadOnlyList<CatalogProduct>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    Task<CatalogProduct?> GetAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Titles of active products, for showing names only (e.g. products of interest); other IDs are missing.</summary>
    Task<IReadOnlyDictionary<Guid, string>> TitlesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken);
}
