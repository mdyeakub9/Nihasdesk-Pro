using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Commerce.Features.Products;

/// <summary>
/// The product editor's API (ADR 0018). Everything goes through the tenant-scoped context, so other
/// workspaces' products are simply not found (SEC-06).
/// </summary>
internal sealed class ProductsHandler(CommerceDbContext db, ITenantContext tenant)
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 100;

    public async Task<ProductPageResponse> ListAsync(string? query, string? status, string? cursor, int? limit, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();
        if (status is not null && CatalogInput.Statuses.TryGetValue(status, out var wanted))
        {
            products = products.Where(p => p.Status == wanted);
        }

        if (CatalogInput.Clean(query) is { } text)
        {
            // Parameterized ILIKE; the user's % and _ are matched literally (SEC-01).
            var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            products = products.Where(p =>
                EF.Functions.ILike(p.Title, pattern)
                || EF.Functions.ILike(p.Handle, pattern)
                || (p.Vendor != null && EF.Functions.ILike(p.Vendor, pattern))
                || (p.Category != null && EF.Functions.ILike(p.Category, pattern))
                || p.Variants.Any(v => v.Sku != null && EF.Functions.ILike(v.Sku, pattern)));
        }

        if (TryDecode(cursor, out var after))
        {
            products = products.Where(p =>
                EF.Functions.LessThan(ValueTuple.Create(p.CreatedAt, p.Id), ValueTuple.Create(after.At, after.Id)));
        }

        var size = limit ?? DefaultPageSize;
        var page = await products
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Take(size + 1)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Handle,
                p.Status,
                p.ProductType,
                p.Category,
                p.Vendor,
                p.CreatedAt,
                p.UpdatedAt,
                VariantCount = p.Variants.Count,
                PriceMin = p.Variants.Min(v => (long?)v.Price),
                PriceMax = p.Variants.Max(v => (long?)v.Price),
                Currency = p.Variants.OrderBy(v => v.Position).Select(v => v.Currency).FirstOrDefault(),
                Tracked = p.Variants.Any(v => v.TrackInventory),
                Stock = p.Variants.Where(v => v.TrackInventory).Sum(v => v.Stock ?? 0),
            })
            .ToListAsync(ct);

        var items = page.Take(size).ToList();
        var next = page.Count > size ? Encode(items[^1].CreatedAt, items[^1].Id) : null;
        return new ProductPageResponse(
            items.Select(p => new ProductSummaryResponse(
                p.Id, p.Title, p.Handle, CatalogInput.Text(p.Status), p.ProductType, p.Category, p.Vendor,
                p.VariantCount, p.PriceMin, p.PriceMax, p.Currency, p.Tracked ? p.Stock : null, p.UpdatedAt)).ToList(),
            next);
    }

    public async Task<ProductResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Include(p => p.Variants).SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? null : ToResponse(product);
    }

    public async Task<Result<ProductResponse>> CreateAsync(ProductRequest request, CancellationToken ct)
    {
        var data = CatalogInput.Product(request);
        var variants = request.Variants!.Select(CatalogInput.Variant).ToList();
        if (await SkusTakenAsync(variants, productId: null, ct) is { Count: > 0 } taken)
        {
            return CatalogErrors.SkuTaken(taken);
        }

        string handle;
        if (CatalogInput.Clean(request.Handle) is { } chosen)
        {
            if (await db.Products.AnyAsync(p => p.Handle == chosen, ct))
            {
                return CatalogErrors.HandleTaken;
            }

            handle = chosen;
        }
        else
        {
            handle = await FreeHandleAsync(Product.HandleFrom(data.Title), ct);
        }

        var product = Product.Create(tenant.TenantId, handle, data);
        product.ReplaceVariants(variants.Select(v => ((Guid?)null, v)).ToList());
        db.Products.Add(product);
        return await SaveAsync(product, ct);
    }

    public async Task<Result<ProductResponse>> UpdateAsync(Guid id, ProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Variants).SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return CatalogErrors.NotFound;
        }

        var variants = request.Variants!.Select(v => (v.Id, Data: CatalogInput.Variant(v))).ToList();
        if (variants.Any(v => v.Id is { } vid && product.Variants.All(existing => existing.Id != vid)))
        {
            // A variant ID from another product (or workspace) is treated like any unknown ID.
            return CatalogErrors.NotFound;
        }

        if (await SkusTakenAsync(variants.Select(v => v.Data).ToList(), product.Id, ct) is { Count: > 0 } taken)
        {
            return CatalogErrors.SkuTaken(taken);
        }

        if (CatalogInput.Clean(request.Handle) is { } handle && handle != product.Handle)
        {
            // Handles stay as created; changing one would break links (and Shopify sync) later.
            return Error.Validation("catalog.handle_fixed", "A product's handle can't be changed.");
        }

        product.Update(CatalogInput.Product(request));
        product.ReplaceVariants(variants.Select(v => (v.Id, v.Data)).ToList());
        return await SaveAsync(product, ct);
    }

    /// <summary>The first free handle: <c>bag</c>, <c>bag-2</c>, <c>bag-3</c>, …</summary>
    internal async Task<string> FreeHandleAsync(string handle, CancellationToken ct)
    {
        var taken = await db.Products
            .Where(p => p.Handle == handle || p.Handle.StartsWith(handle + "-"))
            .Select(p => p.Handle)
            .ToListAsync(ct);
        if (!taken.Contains(handle, StringComparer.Ordinal))
        {
            return handle;
        }

        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{handle}-{n}");
            if (!taken.Contains(candidate, StringComparer.Ordinal))
            {
                return candidate;
            }
        }
    }

    /// <summary>SKUs of <paramref name="variants"/> already used by another product of this workspace.</summary>
    internal async Task<List<string>> SkusTakenAsync(IReadOnlyCollection<VariantData> variants, Guid? productId, CancellationToken ct)
    {
        var skus = variants.Select(v => v.Sku).OfType<string>().ToList();
        if (skus.Count == 0)
        {
            return [];
        }

        return await db.Variants
            .Where(v => v.Sku != null && skus.Contains(v.Sku) && (productId == null || v.ProductId != productId))
            .Select(v => v.Sku!)
            .ToListAsync(ct);
    }

    private async Task<Result<ProductResponse>> SaveAsync(Product product, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } unique)
        {
            // Lost a race with another save: same answers as the checks above.
            return unique.ConstraintName?.Contains("handle", StringComparison.Ordinal) == true
                ? CatalogErrors.HandleTaken
                : CatalogErrors.SkuTaken(product.Variants.Select(v => v.Sku).OfType<string>());
        }

        return Result.Success(ToResponse(product));
    }

    internal static ProductResponse ToResponse(Product p) => new(
        p.Id,
        p.Title,
        p.Handle,
        p.Description,
        CatalogInput.Text(p.Status),
        p.ProductType,
        p.Category,
        p.Vendor,
        p.Tags,
        p.OptionNames,
        p.Variants.OrderBy(v => v.Position).Select(v => new VariantResponse(
            v.Id, v.Sku, v.Barcode, v.Option1, v.Option2, v.Option3, v.Price, v.CompareAtPrice, v.Cost, v.Currency,
            v.TrackInventory, v.Stock, CatalogInput.Text(v.InventoryPolicy), v.WeightGrams, v.RequiresShipping, v.Available)).ToList(),
        p.CreatedAt,
        p.UpdatedAt);

    private static string Encode(DateTimeOffset at, Guid id) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{at.UtcTicks}.{id:N}")));

    internal static bool TryDecode(string? cursor, out (DateTimeOffset At, Guid Id) value)
    {
        value = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 100)
        {
            return false;
        }

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('.');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is > 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
