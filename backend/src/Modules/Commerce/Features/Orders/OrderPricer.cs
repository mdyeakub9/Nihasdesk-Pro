using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.Modules.Shipping.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Features.Orders;

/// <summary>
/// Order totals from the catalog and the shipping zone, by code (CLAUDE.md rule 5, SEC-41): active
/// products only, enough stock for the quantity, one currency, free shipping from the zone's threshold.
/// </summary>
internal sealed class OrderPricer(CommerceDbContext db, IShippingRates shipping)
{
    public async Task<Result<OrderPricing>> PriceAsync(IReadOnlyList<OrderItemRequest> items, Guid shippingZoneId, CancellationToken ct)
    {
        // The same variant twice is one line.
        var wanted = items
            .GroupBy(i => (i.ProductId, i.VariantId))
            .Select(g => (g.Key.ProductId, g.Key.VariantId, Quantity: g.Sum(i => i.Quantity)))
            .ToList();
        var variantIds = wanted.Select(w => w.VariantId).ToList();
        var rows = await db.Variants.AsNoTracking()
            .Where(v => variantIds.Contains(v.Id))
            .Join(db.Products.AsNoTracking().Where(p => p.Status == ProductStatus.Active), v => v.ProductId, p => p.Id, (v, p) => new { Variant = v, p.Title })
            .ToListAsync(ct);

        var lines = new List<OrderLine>();
        foreach (var (productId, variantId, quantity) in wanted)
        {
            var row = rows.SingleOrDefault(r => r.Variant.Id == variantId && r.Variant.ProductId == productId);
            if (row is null)
            {
                return OrderErrors.ProductNotFound;
            }

            if (!HasStock(row.Variant, quantity))
            {
                return OrderErrors.NotAvailable;
            }

            var v = row.Variant;
            var variant = string.Join(" / ", new[] { v.Option1, v.Option2, v.Option3 }.Where(o => !string.IsNullOrWhiteSpace(o)));
            lines.Add(new OrderLine(productId, variantId, row.Title, variant, v.Sku, v.Price, quantity));
        }

        var currency = rows[0].Variant.Currency;
        if (rows.Any(r => r.Variant.Currency != currency))
        {
            return OrderErrors.MixedCurrency;
        }

        var zone = (await shipping.ListAsync(ct)).SingleOrDefault(z => z.Id == shippingZoneId);
        if (zone is null)
        {
            return OrderErrors.ZoneNotFound;
        }

        if (zone.Currency != currency)
        {
            return OrderErrors.MixedCurrency;
        }

        var subtotal = lines.Sum(l => l.UnitPrice * l.Quantity);
        var shippingCost = zone.FreeOver is { } freeOver && subtotal >= freeOver ? 0 : zone.Price;
        return Result.Success(new OrderPricing(lines, zone.Id, zone.Name, shippingCost, subtotal, subtotal + shippingCost, currency));
    }

    /// <summary>Same rule as <see cref="ProductVariant.Available"/>, for a quantity.</summary>
    internal static bool HasStock(ProductVariant variant, int quantity) =>
        !variant.TrackInventory || variant.InventoryPolicy == InventoryPolicy.Continue || variant.Stock >= quantity;
}
