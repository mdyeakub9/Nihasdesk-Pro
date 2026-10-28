using System.Globalization;
using System.Text;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Domain;

/// <summary>Shopify's product statuses (ADR 0018). Only <c>active</c> products are visible to the AI.</summary>
internal enum ProductStatus
{
    Draft,
    Active,
    Archived,
}

/// <summary>Product fields as entered by hand or imported (already validated).</summary>
internal sealed record ProductData(
    string Title,
    string? Description,
    ProductStatus Status,
    string? ProductType,
    string? Category,
    string? Vendor,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> OptionNames);

/// <summary>A product with its variants (ADR 0018). The aggregate root: variants change only through it.</summary>
internal sealed class Product : TenantEntity
{
    public const int TitleMaxLength = 255;
    public const int HandleMaxLength = 255;
    public const int DescriptionMaxLength = 10_000;
    public const int TypeMaxLength = 100;
    public const int CategoryMaxLength = 255;
    public const int VendorMaxLength = 100;
    public const int TagMaxLength = 50;
    public const int MaxTags = 50;
    public const int OptionNameMaxLength = 50;
    public const int MaxOptions = 3;
    public const int MaxVariants = 100;

    private readonly List<ProductVariant> _variants = [];

    private Product(Guid tenantId, string handle)
        : base(tenantId)
    {
        Handle = handle;
    }

    public string Title { get; private set; } = string.Empty;

    /// <summary>URL-style name, unique per workspace (e.g. <c>classic-leather-bag</c>).</summary>
    public string Handle { get; private set; }

    /// <summary>Plain text only; HTML is never stored or rendered (SEC-02).</summary>
    public string? Description { get; private set; }

    public ProductStatus Status { get; private set; }

    public string? ProductType { get; private set; }

    public string? Category { get; private set; }

    public string? Vendor { get; private set; }

    public List<string> Tags { get; private set; } = [];

    /// <summary>Up to three option names (e.g. Size, Colour); each variant has a value per name.</summary>
    public List<string> OptionNames { get; private set; } = [];

    public IReadOnlyList<ProductVariant> Variants => _variants;

    public static Product Create(Guid tenantId, string handle, ProductData data)
    {
        var product = new Product(tenantId, CheckHandle(handle));
        product.Update(data);
        return product;
    }

    public void Update(ProductData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(data.Title) || data.Title.Length > TitleMaxLength)
        {
            throw new ArgumentException("Title is required and at most 255 characters.", nameof(data));
        }

        if (data.OptionNames.Count > MaxOptions)
        {
            throw new ArgumentException("At most three options.", nameof(data));
        }

        Title = data.Title.Trim();
        Description = data.Description;
        Status = data.Status;
        ProductType = data.ProductType;
        Category = data.Category;
        Vendor = data.Vendor;
        Tags = [.. data.Tags];
        OptionNames = [.. data.OptionNames];
    }

    /// <summary>Manual edit: the given list is the new set of variants (by ID; new ones have none).</summary>
    public void ReplaceVariants(IReadOnlyList<(Guid? Id, VariantData Data)> variants)
    {
        ArgumentNullException.ThrowIfNull(variants);
        CheckCount(variants.Count);

        var keep = variants.Where(v => v.Id is not null).Select(v => v.Id!.Value).ToHashSet();
        _variants.RemoveAll(v => !keep.Contains(v.Id));

        var position = 1;
        foreach (var (id, data) in variants)
        {
            var existing = id is { } known ? _variants.SingleOrDefault(v => v.Id == known) : null;
            if (id is not null && existing is null)
            {
                throw new ArgumentException("Variant does not belong to this product.", nameof(variants));
            }

            if (existing is null)
            {
                _variants.Add(ProductVariant.Create(TenantId, Id, data, position));
            }
            else
            {
                existing.Update(data, position);
            }

            position++;
        }
    }

    /// <summary>Import: update the variant with this SKU (or these option values), else add it. Others stay.</summary>
    public ProductVariant UpsertVariant(VariantData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var match = FindVariant(data);
        if (match is not null)
        {
            match.Update(data, match.Position);
            return match;
        }

        CheckCount(_variants.Count + 1);
        var variant = ProductVariant.Create(TenantId, Id, data, _variants.Count + 1);
        _variants.Add(variant);
        return variant;
    }

    public ProductVariant? FindVariant(VariantData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.Sku is { } sku
            ? _variants.SingleOrDefault(v => string.Equals(v.Sku, sku, StringComparison.OrdinalIgnoreCase))
            : _variants.FirstOrDefault(v => v.Sku is null && v.HasOptions(data.Option1, data.Option2, data.Option3));
    }

    /// <summary><c>Classic Leather Bag!</c> → <c>classic-leather-bag</c>; never empty.</summary>
    public static string HandleFrom(string text)
    {
        var normalized = (text ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var handle = builder.ToString().Trim('-');
        if (handle.Length > HandleMaxLength - 10)
        {
            handle = handle[..(HandleMaxLength - 10)].TrimEnd('-');
        }

        return handle.Length > 0 ? handle : "product";
    }

    private static string CheckHandle(string handle)
    {
        if (string.IsNullOrEmpty(handle) || handle.Length > HandleMaxLength || HandleFrom(handle) != handle)
        {
            throw new ArgumentException("Handle must be lower-case letters, digits and dashes.", nameof(handle));
        }

        return handle;
    }

    private static void CheckCount(int count)
    {
        if (count is < 1 or > MaxVariants)
        {
            throw new ArgumentException("A product has 1 to 100 variants.", nameof(count));
        }
    }
}
