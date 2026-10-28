using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Domain;

/// <summary>What happens when a tracked variant is out of stock (Shopify's inventory policy).</summary>
internal enum InventoryPolicy
{
    /// <summary>Stop selling at 0.</summary>
    Deny,

    /// <summary>Keep selling (backorder).</summary>
    Continue,
}

/// <summary>Variant fields as entered by hand or imported (already validated). Money in minor units.</summary>
internal sealed record VariantData(
    string? Sku,
    string? Barcode,
    string? Option1,
    string? Option2,
    string? Option3,
    long Price,
    long? CompareAtPrice,
    long? Cost,
    string Currency,
    bool TrackInventory,
    int? Stock,
    InventoryPolicy InventoryPolicy,
    int? WeightGrams,
    bool RequiresShipping);

/// <summary>One sellable version of a product, e.g. size M in black (ADR 0018).</summary>
internal sealed class ProductVariant : TenantEntity
{
    public const int SkuMaxLength = 64;
    public const int BarcodeMaxLength = 64;
    public const int OptionValueMaxLength = 100;
    public const int CurrencyLength = 3;

    /// <summary>€99,999,999.99 — far above any shop price, far below overflow.</summary>
    public const long MaxAmount = 9_999_999_999;

    public const int MaxStock = 1_000_000_000;

    public const int MaxWeightGrams = 1_000_000;

    private ProductVariant(Guid tenantId, Guid productId)
        : base(tenantId)
    {
        ProductId = productId;
    }

    public Guid ProductId { get; private set; }

    public string? Sku { get; private set; }

    public string? Barcode { get; private set; }

    public string? Option1 { get; private set; }

    public string? Option2 { get; private set; }

    public string? Option3 { get; private set; }

    /// <summary>Selling price in minor units (cents).</summary>
    public long Price { get; private set; }

    /// <summary>Price before a sale, shown struck through; above <see cref="Price"/> when set.</summary>
    public long? CompareAtPrice { get; private set; }

    /// <summary>What the shop paid. Team only: never shown to customers or the AI.</summary>
    public long? Cost { get; private set; }

    /// <summary>ISO 4217 code, e.g. EUR.</summary>
    public string Currency { get; private set; } = "EUR";

    public bool TrackInventory { get; private set; }

    /// <summary>Units in stock when tracked; null when not tracked.</summary>
    public int? Stock { get; private set; }

    public InventoryPolicy InventoryPolicy { get; private set; }

    public int? WeightGrams { get; private set; }

    public bool RequiresShipping { get; private set; } = true;

    public int Position { get; private set; }

    /// <summary>Whether a customer can order it now (the AI uses this, never a guess).</summary>
    public bool Available => !TrackInventory || InventoryPolicy == InventoryPolicy.Continue || Stock > 0;

    public static ProductVariant Create(Guid tenantId, Guid productId, VariantData data, int position)
    {
        var variant = new ProductVariant(tenantId, productId);
        variant.Update(data, position);
        return variant;
    }

    public void Update(VariantData data, int position)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Price is < 0 or > MaxAmount
            || data.CompareAtPrice is < 0 or > MaxAmount
            || data.Cost is < 0 or > MaxAmount
            || data.Stock is < -MaxStock or > MaxStock
            || data.WeightGrams is < 0 or > MaxWeightGrams
            || data.Currency.Length != CurrencyLength)
        {
            throw new ArgumentException("Variant values out of range.", nameof(data));
        }

        Sku = data.Sku;
        Barcode = data.Barcode;
        Option1 = data.Option1;
        Option2 = data.Option2;
        Option3 = data.Option3;
        Price = data.Price;
        CompareAtPrice = data.CompareAtPrice;
        Cost = data.Cost;
        Currency = data.Currency;
        TrackInventory = data.TrackInventory;
        Stock = data.TrackInventory ? data.Stock ?? 0 : null;
        InventoryPolicy = data.InventoryPolicy;
        WeightGrams = data.WeightGrams;
        RequiresShipping = data.RequiresShipping;
        Position = position;
    }

    public bool HasOptions(string? option1, string? option2, string? option3) =>
        Same(Option1, option1) && Same(Option2, option2) && Same(Option3, option3);

    private static bool Same(string? a, string? b) => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
