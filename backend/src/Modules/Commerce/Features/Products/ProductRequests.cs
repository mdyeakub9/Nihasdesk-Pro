using System.Text.RegularExpressions;
using FluentValidation;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Features.Images;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Features.Products;

/// <summary>A variant in a create/update request. Money in minor units (8900 = €89.00). No ID = new variant.</summary>
internal sealed record VariantRequest(
    Guid? Id,
    string? Sku,
    string? Barcode,
    string? Option1,
    string? Option2,
    string? Option3,
    long Price,
    long? CompareAtPrice,
    long? Cost,
    string? Currency,
    bool TrackInventory,
    int? Stock,
    string? InventoryPolicy,
    int? WeightGrams,
    bool? RequiresShipping,
    // A photo of the same product (ADR 0031 §7); null or left out = the main photo.
    Guid? ImageId = null);

/// <summary>
/// Create or replace a product with its variants (ADR 0018). The handle is derived from the title when
/// empty. IDs, tenant and timestamps are never taken from the client (SEC-22).
/// </summary>
internal sealed record ProductRequest(
    string? Title,
    string? Handle,
    string? Description,
    string? Status,
    string? ProductType,
    string? Category,
    string? Vendor,
    List<string>? Tags,
    List<string>? OptionNames,
    List<VariantRequest>? Variants);

internal sealed record VariantResponse(
    Guid Id,
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
    string InventoryPolicy,
    int? WeightGrams,
    bool RequiresShipping,
    bool Available,
    Guid? ImageId);

internal sealed record ProductResponse(
    Guid Id,
    string Title,
    string Handle,
    string? Description,
    string Status,
    string? ProductType,
    string? Category,
    string? Vendor,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> OptionNames,
    IReadOnlyList<VariantResponse> Variants,
    // In position order; the first saved one is the main photo.
    IReadOnlyList<ProductImageResponse> Images,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record ProductSummaryResponse(
    Guid Id,
    string Title,
    string Handle,
    string Status,
    string? ProductType,
    string? Category,
    string? Vendor,
    int VariantCount,
    long? PriceMin,
    long? PriceMax,
    string? Currency,
    // Sum over tracked variants; null when none is tracked.
    int? Stock,
    DateTimeOffset UpdatedAt,
    // The main photo's thumbnail (signed-in members only), or null.
    string? ThumbnailUrl);

internal sealed record ProductPageResponse(IReadOnlyList<ProductSummaryResponse> Items, string? NextCursor);

internal static class CatalogErrors
{
    /// <summary>Also for other workspaces' IDs (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("catalog.product_not_found", "Product not found.");

    public static readonly Error HandleTaken = Error.Conflict("catalog.handle_taken", "Another product already uses this handle.");

    public static Error SkuTaken(IEnumerable<string> skus) =>
        Error.Conflict("catalog.sku_taken", "SKU already used by another product: " + string.Join(", ", skus.Take(10)));
}

/// <summary>Turns validated input into domain data; shared by the editor and the CSV import.</summary>
internal static partial class CatalogInput
{
    public const string DefaultCurrency = "EUR";

    public static readonly IReadOnlyDictionary<string, ProductStatus> Statuses = new Dictionary<string, ProductStatus>(StringComparer.Ordinal)
    {
        ["draft"] = ProductStatus.Draft,
        ["active"] = ProductStatus.Active,
        ["archived"] = ProductStatus.Archived,
    };

    public static readonly IReadOnlyDictionary<string, InventoryPolicy> Policies = new Dictionary<string, InventoryPolicy>(StringComparer.Ordinal)
    {
        ["deny"] = InventoryPolicy.Deny,
        ["continue"] = InventoryPolicy.Continue,
    };

    public static string Text<TEnum>(TEnum value)
        where TEnum : struct, Enum => value.ToString().ToLowerInvariant();

    public static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public static List<string> CleanList(IEnumerable<string>? values) =>
        (values ?? [])
            .Select(Clean)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static ProductData Product(ProductRequest request) => new(
        Clean(request.Title)!,
        Clean(request.Description),
        request.Status is { } status ? Statuses[status] : ProductStatus.Active,
        Clean(request.ProductType),
        Clean(request.Category),
        Clean(request.Vendor),
        CleanList(request.Tags),
        CleanList(request.OptionNames));

    public static VariantData Variant(VariantRequest v) => new(
        Clean(v.Sku),
        Clean(v.Barcode),
        Clean(v.Option1),
        Clean(v.Option2),
        Clean(v.Option3),
        v.Price,
        v.CompareAtPrice,
        v.Cost,
        Clean(v.Currency)?.ToUpperInvariant() ?? DefaultCurrency,
        v.TrackInventory,
        v.Stock,
        v.InventoryPolicy is { } policy ? Policies[policy] : InventoryPolicy.Deny,
        v.WeightGrams,
        v.RequiresShipping ?? true);

    public static bool IsCurrency(string? value) => value is null || CurrencyPattern().IsMatch(value);

    public static bool IsHandle(string? value) =>
        value is null || (value.Length <= Domain.Product.HandleMaxLength && Domain.Product.HandleFrom(value) == value);

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();

    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$")]
    public static partial Regex BarcodePattern();
}

internal sealed class ProductValidator : AbstractValidator<ProductRequest>
{
    public ProductValidator()
    {
        RuleFor(r => r.Title).NotEmpty().Must(t => !string.IsNullOrWhiteSpace(t)).MaximumLength(Product.TitleMaxLength);
        RuleFor(r => r.Handle).Must(h => CatalogInput.IsHandle(CatalogInput.Clean(h)))
            .WithMessage("Handle may contain lower-case letters, digits and dashes only.");
        RuleFor(r => r.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(r => r.Status).Must(s => s is null || CatalogInput.Statuses.ContainsKey(s))
            .WithMessage("Status must be draft, active or archived.");
        RuleFor(r => r.ProductType).MaximumLength(Product.TypeMaxLength);
        RuleFor(r => r.Category).MaximumLength(Product.CategoryMaxLength);
        RuleFor(r => r.Vendor).MaximumLength(Product.VendorMaxLength);
        RuleFor(r => r.Tags).Must(t => t is null || t.Count <= Product.MaxTags).WithMessage("At most 50 tags.");
        RuleForEach(r => r.Tags).NotEmpty().MaximumLength(Product.TagMaxLength);
        RuleFor(r => r.OptionNames).Must(o => o is null || o.Count <= Product.MaxOptions).WithMessage("At most three options.");
        RuleFor(r => r.OptionNames)
            .Must(o => o is null || CatalogInput.CleanList(o).Count == o.Count)
            .WithMessage("Option names must be different and not empty.");
        RuleForEach(r => r.OptionNames).MaximumLength(Product.OptionNameMaxLength);

        RuleFor(r => r.Variants).NotEmpty().Must(v => v is null || v.Count <= Product.MaxVariants)
            .WithMessage("A product has 1 to 100 variants.");
        RuleFor(r => r.Variants)
            .Must(v => v is null || v.Select(x => CatalogInput.Clean(x.Sku)).OfType<string>().GroupBy(s => s, StringComparer.OrdinalIgnoreCase).All(g => g.Count() == 1))
            .WithMessage("Each SKU may appear once.");
        RuleFor(r => r)
            .Must(r => r.Variants is null || r.Variants
                .Select(v => string.Join('\u001f', CatalogInput.Clean(v.Option1), CatalogInput.Clean(v.Option2), CatalogInput.Clean(v.Option3)).ToUpperInvariant())
                .Distinct()
                .Count() == r.Variants.Count)
            .WithName(nameof(ProductRequest.Variants))
            .WithMessage("Each variant needs a different combination of option values.");
        RuleForEach(r => r.Variants).SetValidator(r => new VariantValidator(CatalogInput.CleanList(r.OptionNames).Count));
    }
}

internal sealed class VariantValidator : AbstractValidator<VariantRequest>
{
    public VariantValidator(int optionCount)
    {
        RuleFor(v => v.Sku).MaximumLength(ProductVariant.SkuMaxLength);
        RuleFor(v => v.Barcode).Must(b => CatalogInput.Clean(b) is not { } code || CatalogInput.BarcodePattern().IsMatch(code))
            .WithMessage("Barcode may contain letters, digits and dashes (max 64).");
        Option(v => v.Option1, 1, optionCount);
        Option(v => v.Option2, 2, optionCount);
        Option(v => v.Option3, 3, optionCount);
        RuleFor(v => v.Price).InclusiveBetween(0, ProductVariant.MaxAmount);
        RuleFor(v => v.CompareAtPrice).InclusiveBetween(0, ProductVariant.MaxAmount)
            .GreaterThan(v => v.Price).When(v => v.CompareAtPrice is not null)
            .WithMessage("The compare-at price must be higher than the price.");
        RuleFor(v => v.Cost).InclusiveBetween(0, ProductVariant.MaxAmount);
        RuleFor(v => v.Currency).Must(c => CatalogInput.IsCurrency(CatalogInput.Clean(c)?.ToUpperInvariant()))
            .WithMessage("Currency must be an ISO code like EUR.");
        RuleFor(v => v.Stock).InclusiveBetween(-ProductVariant.MaxStock, ProductVariant.MaxStock);
        RuleFor(v => v.InventoryPolicy).Must(p => p is null || CatalogInput.Policies.ContainsKey(p))
            .WithMessage("Inventory policy must be deny or continue.");
        RuleFor(v => v.WeightGrams).InclusiveBetween(0, ProductVariant.MaxWeightGrams);
    }

    /// <summary>A value for each option name the product has, none beyond.</summary>
    private void Option(System.Linq.Expressions.Expression<Func<VariantRequest, string?>> value, int index, int optionCount)
    {
        if (index <= optionCount)
        {
            RuleFor(value).Must(v => CatalogInput.Clean(v) is not null).WithMessage($"Option {index} needs a value.")
                .MaximumLength(ProductVariant.OptionValueMaxLength);
        }
        else
        {
            RuleFor(value).Must(v => CatalogInput.Clean(v) is null).WithMessage($"This product has no option {index}.");
        }
    }
}
