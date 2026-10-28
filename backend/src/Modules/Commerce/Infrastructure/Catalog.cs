using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Domain;

namespace Nihasdesk.Modules.Commerce.Infrastructure;

internal sealed class Catalog(CommerceDbContext db) : ICatalog
{
    public const int MaxResults = 20;

    public async Task<IReadOnlyList<CatalogProduct>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length is 0 or > 200)
        {
            return [];
        }

        var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var products = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .Where(p => p.Status == ProductStatus.Active)
            .Where(p =>
                EF.Functions.ILike(p.Title, pattern)
                || (p.Category != null && EF.Functions.ILike(p.Category, pattern))
                || (p.Vendor != null && EF.Functions.ILike(p.Vendor, pattern))
                || p.Tags.Any(t => EF.Functions.ILike(t, pattern))
                || p.Variants.Any(v => v.Sku != null && EF.Functions.ILike(v.Sku, pattern)))
            .OrderBy(p => p.Title)
            .Take(Math.Clamp(limit, 1, MaxResults))
            .ToListAsync(cancellationToken);
        return products.Select(ToContract).ToList();
    }

    public async Task<CatalogProduct?> GetAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Variants)
            .SingleOrDefaultAsync(p => p.Id == productId && p.Status == ProductStatus.Active, cancellationToken);
        return product is null ? null : ToContract(product);
    }

    private static CatalogProduct ToContract(Product p) => new(
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
                v.TrackInventory ? v.Stock : null))
            .ToList());
}
