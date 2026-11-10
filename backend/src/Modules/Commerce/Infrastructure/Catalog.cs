using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Domain;

namespace Nihasdesk.Modules.Commerce.Infrastructure;

internal sealed partial class Catalog(CommerceDbContext db) : ICatalog
{
    public const int MaxResults = 20;
    private const int MaxCandidates = 500;
    private const int MaxWords = 8;

    // Words that say nothing about a product, in the dashboard's languages.
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "you", "have", "how", "much", "con", "per", "del", "della", "dei", "che", "avete", "quanto", "costa",
    };

    public async Task<IReadOnlyList<CatalogProduct>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length is 0 or > 200)
        {
            return [];
        }

        var stems = Stems(text);
        // Only short words or stop words ("XL"): the whole text as typed. Stems are letters and digits, safe in a pattern.
        var patterns = stems.Count > 0
            ? stems.Select(s => $"%{s}%").ToArray()
            : ["%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%"];

        var candidates = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Where(p => p.Status == ProductStatus.Active)
            .Where(p =>
                patterns.Any(x => EF.Functions.ILike(p.Title, x))
                || (p.ProductType != null && patterns.Any(x => EF.Functions.ILike(p.ProductType, x)))
                || (p.Category != null && patterns.Any(x => EF.Functions.ILike(p.Category, x)))
                || (p.Vendor != null && patterns.Any(x => EF.Functions.ILike(p.Vendor, x)))
                || (p.Description != null && patterns.Any(x => EF.Functions.ILike(p.Description, x)))
                || p.Tags.Any(t => patterns.Any(x => EF.Functions.ILike(t, x)))
                || p.Variants.Any(v =>
                    (v.Sku != null && patterns.Any(x => EF.Functions.ILike(v.Sku, x)))
                    || (v.Option1 != null && patterns.Any(x => EF.Functions.ILike(v.Option1, x)))
                    || (v.Option2 != null && patterns.Any(x => EF.Functions.ILike(v.Option2, x)))
                    || (v.Option3 != null && patterns.Any(x => EF.Functions.ILike(v.Option3, x)))))
            .OrderBy(p => p.Title)
            .Take(MaxCandidates)
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(p => (Product: p, Rank: Rank(p, stems)))
            .OrderByDescending(x => x.Rank.Words)
            .ThenByDescending(x => x.Rank.Score)
            .ThenBy(x => x.Product.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, MaxResults))
            .Select(x => x.Product)
            .ToList();
        var images = await SavedImagesAsync(ranked.Select(p => p.Id).ToList(), cancellationToken);
        return ranked.Select(p => ToContract(p, images)).ToList();
    }

    /// <summary>
    /// Word starts for matching: letters and digits only, no stop words, without a last vowel or
    /// "s", so "borse" finds "Borsa" and "nera" finds "Nero". At most <see cref="MaxWords"/>.
    /// </summary>
    internal static List<string> Stems(string text) =>
        WordPattern().Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => !StopWords.Contains(w))
            .Select(w => w.Length >= 4 && "aeiouàèéìòùs".Contains(w[^1], StringComparison.Ordinal) ? w[..^1] : w)
            .Distinct()
            .Take(MaxWords)
            .ToList();

    /// <summary>How many words of the query the product has; a word in the title counts most.</summary>
    private static (int Words, int Score) Rank(Product p, List<string> stems)
    {
        bool Has(string? value, string stem) => value?.Contains(stem, StringComparison.OrdinalIgnoreCase) == true;
        int words = 0, score = 0;
        foreach (var stem in stems)
        {
            var points = Has(p.Title, stem) ? 4
                : p.Tags.Any(t => Has(t, stem)) || p.Variants.Any(v => Has(v.Sku, stem) || Has(v.Option1, stem) || Has(v.Option2, stem) || Has(v.Option3, stem)) ? 3
                : Has(p.ProductType, stem) || Has(p.Category, stem) || Has(p.Vendor, stem) ? 2
                : Has(p.Description, stem) ? 1
                : 0;
            words += points > 0 ? 1 : 0;
            score += points;
        }

        return (words, score);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]{3,}")]
    private static partial Regex WordPattern();

    public async Task<CatalogProduct?> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .SingleOrDefaultAsync(p => p.Id == productId && p.Status == ProductStatus.Active, cancellationToken);
        return product is null ? null : ToContract(product, await SavedImagesAsync([product.Id], cancellationToken));
    }

    public async Task<IReadOnlyDictionary<Guid, string>> TitlesAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await db.Products
            .AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.Status == ProductStatus.Active)
            .ToDictionaryAsync(p => p.Id, p => p.Title, cancellationToken);
    }

    /// <summary>Saved images only (ADR 0031 §7), by product in position order.</summary>
    private async Task<ILookup<Guid, Guid>> SavedImagesAsync(List<Guid> productIds, CancellationToken ct) =>
        (await db.ProductImages
            .AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId) && i.Status == ProductImageStatus.Saved)
            .OrderBy(i => i.Position)
            .Select(i => new { i.ProductId, i.Id })
            .ToListAsync(ct))
        .ToLookup(i => i.ProductId, i => i.Id);

    private static CatalogProduct ToContract(Product p, ILookup<Guid, Guid> images) => new(
        p.Id,
        p.Title,
        p.Description,
        p.Category,
        p.Vendor,
        p.Tags,
        p.OptionNames,
        p.Variants
            .OrderBy(v => v.Position)
            .Select(v => new CatalogVariant(
                v.Id,
                v.Sku,
                new[] { v.Option1, v.Option2, v.Option3 }.Take(p.OptionNames.Count).Select(o => o ?? string.Empty).ToList(),
                v.Price,
                v.CompareAtPrice,
                v.Currency,
                v.Available,
                v.TrackInventory ? v.Stock : null,
                v.ImageId is { } image && images[p.Id].Contains(image) ? image : null))
            .ToList(),
        images[p.Id].Cast<Guid?>().FirstOrDefault());
}
