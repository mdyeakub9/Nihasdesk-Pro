using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Features.Products;

namespace Nihasdesk.Modules.Commerce.Features.Import;

/// <summary>A problem in the file, by CSV line (header = line 1) and column.</summary>
internal sealed record ImportIssue(int Line, string? Column, string Code, string Message);

/// <summary>One product from the file: its fields and its variant rows, with their lines.</summary>
internal sealed record ImportedProduct(string Handle, int Line, ProductData Product, IReadOnlyList<(int Line, VariantData Variant)> Variants);

/// <summary>
/// Reads Shopify's product CSV (ADR 0018). Rows are grouped by Handle; the first row carries the
/// product fields; rows without variant data (image rows) are skipped. Unknown columns are ignored.
/// </summary>
internal static partial class ShopifyCsv
{
    public const int MaxRows = 5000;

    /// <summary>Our template: the Shopify columns we read, in Shopify's order.</summary>
    public static readonly string[] TemplateColumns =
    [
        "Handle", "Title", "Body (HTML)", "Vendor", "Product Category", "Type", "Tags", "Status",
        "Option1 Name", "Option1 Value", "Option2 Name", "Option2 Value", "Option3 Name", "Option3 Value",
        "Variant SKU", "Variant Grams", "Variant Inventory Tracker", "Variant Inventory Qty", "Variant Inventory Policy",
        "Variant Price", "Variant Compare At Price", "Variant Requires Shipping", "Variant Barcode", "Cost per item",
        "Variant Currency",
    ];

    public static (List<ImportedProduct> Products, List<ImportIssue> Issues) Parse(string text)
    {
        var issues = new List<ImportIssue>();
        var (rows, tooMany) = Csv.Read(text, MaxRows);
        if (tooMany)
        {
            issues.Add(new ImportIssue(MaxRows + 2, null, "too_many_rows", $"At most {MaxRows} rows per file."));
            return ([], issues);
        }

        if (rows.Count < 2)
        {
            issues.Add(new ImportIssue(1, null, "empty", "The file has no product rows."));
            return ([], issues);
        }

        var columns = rows[0]
            .Select((name, index) => (Name: name.Trim().TrimStart('﻿'), Index: index))
            .Where(c => c.Name.Length > 0)
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "Handle", "Title", "Variant Price" })
        {
            if (!columns.ContainsKey(required))
            {
                issues.Add(new ImportIssue(1, required, "missing_column", $"The column \"{required}\" is missing."));
            }
        }

        if (issues.Count > 0)
        {
            return ([], issues);
        }

        var products = new List<ImportedProduct>();
        var lines = rows.Skip(1).Select((cells, i) => new Row(cells, columns, i + 2)).ToList();
        foreach (var group in lines.GroupBy(r => r.Get("Handle")?.ToLowerInvariant() ?? string.Empty))
        {
            var first = group.First();
            if (group.Key.Length == 0)
            {
                foreach (var row in group)
                {
                    issues.Add(new ImportIssue(row.Line, "Handle", "required", "Every row needs a Handle."));
                }

                continue;
            }

            if (Product.HandleFrom(group.Key) != group.Key || group.Key.Length > Product.HandleMaxLength)
            {
                issues.Add(new ImportIssue(first.Line, "Handle", "invalid_value", "Handle may contain letters, digits and dashes only."));
                continue;
            }

            if (ReadProduct(first, issues) is not { } product)
            {
                continue;
            }

            var variants = new List<(int, VariantData)>();
            foreach (var row in group.Where(r => r.HasVariant))
            {
                if (ReadVariant(row, product.OptionNames.Count, issues) is { } variant)
                {
                    variants.Add((row.Line, variant));
                }
            }

            CheckVariants(first.Line, variants, issues);
            products.Add(new ImportedProduct(group.Key, first.Line, product, variants));
        }

        var duplicates = products
            .SelectMany(p => p.Variants)
            .Where(v => v.Variant.Sku is not null)
            .GroupBy(v => v.Variant.Sku!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);
        foreach (var duplicate in duplicates)
        {
            foreach (var (line, _) in duplicate.Skip(1))
            {
                issues.Add(new ImportIssue(line, "Variant SKU", "duplicate_sku", $"SKU {duplicate.Key} appears more than once in the file."));
            }
        }

        return (products, issues);
    }

    private static ProductData? ReadProduct(Row row, List<ImportIssue> issues)
    {
        var title = row.Get("Title");
        if (title is null)
        {
            issues.Add(new ImportIssue(row.Line, "Title", "required", "The first row of a product needs a Title."));
            return null;
        }

        var before = issues.Count;
        Max(row, "Title", Product.TitleMaxLength, issues);
        Max(row, "Vendor", Product.VendorMaxLength, issues);
        Max(row, "Type", Product.TypeMaxLength, issues);
        Max(row, "Product Category", Product.CategoryMaxLength, issues);

        var tags = CatalogInput.CleanList((row.Get("Tags") ?? string.Empty).Split(','));
        if (tags.Count > Product.MaxTags || tags.Any(t => t.Length > Product.TagMaxLength))
        {
            issues.Add(new ImportIssue(row.Line, "Tags", "invalid_value", "At most 50 tags of up to 50 characters."));
        }

        // Shopify writes "Title" / "Default Title" for products without options.
        var options = new List<string>();
        for (var i = 1; i <= Product.MaxOptions; i++)
        {
            var name = row.Get($"Option{i} Name");
            if (name is null || (i == 1 && name == "Title" && row.Get("Option1 Value") == "Default Title"))
            {
                continue;
            }

            if (name.Length > Product.OptionNameMaxLength || options.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                issues.Add(new ImportIssue(row.Line, $"Option{i} Name", "invalid_value", "Option names must be different, up to 50 characters."));
            }

            options.Add(name);
        }

        var status = ProductStatus.Active;
        if (row.Get("Status") is { } statusText)
        {
            if (!CatalogInput.Statuses.TryGetValue(statusText.ToLowerInvariant(), out status))
            {
                issues.Add(new ImportIssue(row.Line, "Status", "invalid_value", "Status must be active, draft or archived."));
            }
        }
        else if (row.Get("Published") is { } published)
        {
            status = Bool(published) == false ? ProductStatus.Draft : ProductStatus.Active;
        }

        var description = PlainText(row.Get("Body (HTML)"));
        if (description?.Length > Product.DescriptionMaxLength)
        {
            description = description[..Product.DescriptionMaxLength];
        }

        return issues.Count > before
            ? null
            : new ProductData(title, description, status, row.Get("Type"), row.Get("Product Category"), row.Get("Vendor"), tags, options);
    }

    private static VariantData? ReadVariant(Row row, int optionCount, List<ImportIssue> issues)
    {
        var before = issues.Count;
        var sku = row.Get("Variant SKU");
        Max(row, "Variant SKU", ProductVariant.SkuMaxLength, issues);
        var barcode = row.Get("Variant Barcode");
        if (barcode is not null && !CatalogInput.BarcodePattern().IsMatch(barcode))
        {
            issues.Add(new ImportIssue(row.Line, "Variant Barcode", "invalid_value", "Barcode may contain letters, digits and dashes (max 64)."));
        }

        var values = new string?[Product.MaxOptions];
        for (var i = 1; i <= Product.MaxOptions; i++)
        {
            var value = row.Get($"Option{i} Value");
            if (i == 1 && value == "Default Title" && optionCount == 0)
            {
                value = null;
            }

            if (i <= optionCount && value is null)
            {
                issues.Add(new ImportIssue(row.Line, $"Option{i} Value", "required", $"Option {i} needs a value."));
            }
            else if (i > optionCount && value is not null)
            {
                issues.Add(new ImportIssue(row.Line, $"Option{i} Value", "invalid_value", $"This product has no option {i}."));
            }
            else if (value?.Length > ProductVariant.OptionValueMaxLength)
            {
                issues.Add(new ImportIssue(row.Line, $"Option{i} Value", "too_long", "At most 100 characters."));
            }

            values[i - 1] = value;
        }

        var price = Money(row, "Variant Price", required: true, issues);
        var compareAt = Money(row, "Variant Compare At Price", required: false, issues);
        var cost = Money(row, "Cost per item", required: false, issues);
        if (price is not null && compareAt <= price)
        {
            // Shopify keeps a compare-at price equal to the price; it means "no sale".
            compareAt = null;
        }

        var currency = row.Get("Variant Currency")?.ToUpperInvariant() ?? CatalogInput.DefaultCurrency;
        if (!CatalogInput.IsCurrency(currency))
        {
            issues.Add(new ImportIssue(row.Line, "Variant Currency", "invalid_value", "Currency must be an ISO code like EUR."));
        }

        var track = row.Get("Variant Inventory Tracker") is not null;
        var stock = Integer(row, "Variant Inventory Qty", -ProductVariant.MaxStock, ProductVariant.MaxStock, issues);
        var grams = Integer(row, "Variant Grams", 0, ProductVariant.MaxWeightGrams, issues);
        var policy = InventoryPolicy.Deny;
        if (row.Get("Variant Inventory Policy") is { } policyText && !CatalogInput.Policies.TryGetValue(policyText.ToLowerInvariant(), out policy))
        {
            issues.Add(new ImportIssue(row.Line, "Variant Inventory Policy", "invalid_value", "Inventory policy must be deny or continue."));
        }

        var shipping = true;
        if (row.Get("Variant Requires Shipping") is { } shippingText)
        {
            if (Bool(shippingText) is { } value)
            {
                shipping = value;
            }
            else
            {
                issues.Add(new ImportIssue(row.Line, "Variant Requires Shipping", "invalid_value", "Use TRUE or FALSE."));
            }
        }

        return issues.Count > before || price is null
            ? null
            : new VariantData(sku, barcode, values[0], values[1], values[2], price.Value, compareAt, cost, currency, track, stock, policy, grams, shipping);
    }

    private static void CheckVariants(int line, List<(int Line, VariantData Variant)> variants, List<ImportIssue> issues)
    {
        if (variants.Count == 0)
        {
            issues.Add(new ImportIssue(line, "Variant Price", "no_variant", "The product has no variant row with a price."));
        }
        else if (variants.Count > Product.MaxVariants)
        {
            issues.Add(new ImportIssue(line, null, "too_many_variants", "A product has at most 100 variants."));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (variantLine, v) in variants)
        {
            if (!seen.Add(string.Join('\u001f', v.Option1, v.Option2, v.Option3)))
            {
                issues.Add(new ImportIssue(variantLine, "Option1 Value", "duplicate_options", "Another variant of this product has the same option values."));
            }
        }
    }

    /// <summary>"89.00" or "89,00" → 8900 (two decimals, as EUR).</summary>
    private static long? Money(Row row, string column, bool required, List<ImportIssue> issues)
    {
        var text = row.Get(column);
        if (text is null)
        {
            if (required)
            {
                issues.Add(new ImportIssue(row.Line, column, "required", "A price is required."));
            }

            return null;
        }

        var normalized = text.Contains('.', StringComparison.Ordinal) ? text : text.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || decimal.Round(amount, 2) != amount
            || amount * 100 > ProductVariant.MaxAmount)
        {
            issues.Add(new ImportIssue(row.Line, column, "invalid_number", "Use a number like 89.00."));
            return null;
        }

        return (long)(amount * 100);
    }

    private static int? Integer(Row row, string column, int min, int max, List<ImportIssue> issues)
    {
        var text = row.Get(column);
        if (text is null)
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            issues.Add(new ImportIssue(row.Line, column, "invalid_number", "Use a whole number."));
            return null;
        }

        return value;
    }

    private static void Max(Row row, string column, int max, List<ImportIssue> issues)
    {
        if (row.Get(column)?.Length > max)
        {
            issues.Add(new ImportIssue(row.Line, column, "too_long", $"At most {max} characters."));
        }
    }

    private static bool? Bool(string text) =>
        text.ToUpperInvariant() switch
        {
            "TRUE" or "YES" or "1" => true,
            "FALSE" or "NO" or "0" => false,
            _ => null,
        };

    /// <summary>Shopify's HTML description as plain text: tags dropped, entities decoded (SEC-02).</summary>
    internal static string? PlainText(string? html)
    {
        if (html is null)
        {
            return null;
        }

        var text = BlockTags().Replace(html, "\n");
        text = AnyTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = SpaceRuns().Replace(text, " ");
        text = BlankLines().Replace(text, "\n\n");
        return CatalogInput.Clean(text);
    }

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h[1-6])\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTags();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex SpaceRuns();

    [GeneratedRegex(@"\s*\n\s*\n\s*")]
    private static partial Regex BlankLines();

    private sealed class Row(string[] cells, Dictionary<string, int> columns, int line)
    {
        public int Line { get; } = line;

        public bool HasVariant => Get("Variant Price") is not null || Get("Variant SKU") is not null || Get("Option1 Value") is not null;

        public string? Get(string column) =>
            columns.TryGetValue(column, out var index) && index < cells.Length ? CatalogInput.Clean(cells[index]) : null;
    }
}
