using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Modules.Commerce.Features.Import;

internal sealed record ImportCounts(int ProductsCreated, int ProductsUpdated, int VariantsCreated, int VariantsUpdated);

/// <summary>Preview and apply answer the same: what would change (or changed) and every issue found.</summary>
internal sealed record ImportResponse(bool Applied, int Rows, ImportCounts Counts, IReadOnlyList<ImportIssue> Issues);

internal static class ImportErrors
{
    public static readonly Error NoFile = Error.Validation("catalog.import_no_file", "Choose a CSV file to import.");

    public static readonly Error TooLarge = Error.Validation("catalog.import_too_large", "The file is larger than 2 MB.");

    public static readonly Error NotCsv = Error.Validation("catalog.import_not_csv", "This isn't a CSV text file (UTF-8).");

    public static readonly Error Conflict =
        Error.Conflict("catalog.import_conflict", "The catalog changed during the import. Nothing was saved; please try again.");
}

/// <summary>
/// CSV import (ADR 0018): the file is read in memory, never stored. Preview saves nothing; apply
/// checks again and saves everything in one transaction, or nothing when there is any issue.
/// </summary>
internal sealed class ImportHandler(CommerceDbContext db, ITenantContext tenant)
{
    public const long MaxFileBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads the uploaded file: present, small enough, and really UTF-8 text (SEC-04).</summary>
    public static async Task<Result<string>> ReadFileAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return ImportErrors.NoFile;
        }

        var form = await request.ReadFormAsync(ct);
        if (form.Files.GetFile("file") is not { Length: > 0 } file)
        {
            return ImportErrors.NoFile;
        }

        if (file.Length > MaxFileBytes)
        {
            return ImportErrors.TooLarge;
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        // Checked by content, not by name or the browser's Content-Type: ZIP (xlsx) and other binaries fail here.
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return ImportErrors.NotCsv;
        }

        return text.Contains('\0', StringComparison.Ordinal) || (bytes.Length >= 2 && bytes[0] == 'P' && bytes[1] == 'K')
            ? ImportErrors.NotCsv
            : Result.Success(text.TrimStart('﻿'));
    }

    public async Task<ImportResponse> RunAsync(string text, bool apply, CancellationToken ct)
    {
        var (imported, issues) = ShopifyCsv.Parse(text);
        var rows = Math.Max(0, text.Count(c => c == '\n'));

        var handles = imported.Select(p => p.Handle).ToList();
        var existing = await db.Products.Include(p => p.Variants).Where(p => handles.Contains(p.Handle)).ToListAsync(ct);
        var byHandle = existing.ToDictionary(p => p.Handle, StringComparer.Ordinal);
        await CheckSkusAsync(imported, byHandle, issues, ct);

        var counts = Count(imported, byHandle);
        if (!apply || issues.Count > 0)
        {
            return new ImportResponse(false, rows, counts, Sorted(issues));
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var item in imported)
        {
            if (!byHandle.TryGetValue(item.Handle, out var product))
            {
                product = Product.Create(tenant.TenantId, item.Handle, item.Product);
                db.Products.Add(product);
            }
            else
            {
                product.Update(item.Product);
            }

            foreach (var (_, variant) in item.Variants)
            {
                product.UpsertVariant(variant);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new ImportResponse(false, rows, counts, [new ImportIssue(1, null, ImportErrors.Conflict.Code, ImportErrors.Conflict.Message)]);
        }

        return new ImportResponse(true, rows, counts, []);
    }

    /// <summary>A SKU may move within its product, never take one from another product of the workspace.</summary>
    private async Task CheckSkusAsync(List<ImportedProduct> imported, Dictionary<string, Product> byHandle, List<ImportIssue> issues, CancellationToken ct)
    {
        var skus = imported.SelectMany(p => p.Variants).Select(v => v.Variant.Sku).OfType<string>().ToList();
        if (skus.Count == 0)
        {
            return;
        }

        var owners = await db.Variants
            .Where(v => v.Sku != null && skus.Contains(v.Sku))
            .Join(db.Products, v => v.ProductId, p => p.Id, (v, p) => new { Sku = v.Sku!, p.Handle })
            .ToListAsync(ct);
        foreach (var item in imported)
        {
            foreach (var (line, variant) in item.Variants)
            {
                var owner = owners.FirstOrDefault(o => string.Equals(o.Sku, variant.Sku, StringComparison.Ordinal));
                if (owner is not null && owner.Handle != item.Handle)
                {
                    issues.Add(new ImportIssue(line, "Variant SKU", "sku_taken", $"SKU {variant.Sku} belongs to product \"{owner.Handle}\"."));
                }
            }

            if (byHandle.TryGetValue(item.Handle, out var product)
                && product.Variants.Count + item.Variants.Count(v => product.FindVariant(v.Variant) is null) > Product.MaxVariants)
            {
                issues.Add(new ImportIssue(item.Line, null, "too_many_variants", "With the variants already there, this product would have more than 100."));
            }
        }
    }

    private static ImportCounts Count(List<ImportedProduct> imported, Dictionary<string, Product> byHandle)
    {
        int productsCreated = 0, productsUpdated = 0, variantsCreated = 0, variantsUpdated = 0;
        foreach (var item in imported)
        {
            if (byHandle.TryGetValue(item.Handle, out var product))
            {
                productsUpdated++;
                var updated = item.Variants.Count(v => product.FindVariant(v.Variant) is not null);
                variantsUpdated += updated;
                variantsCreated += item.Variants.Count - updated;
            }
            else
            {
                productsCreated++;
                variantsCreated += item.Variants.Count;
            }
        }

        return new ImportCounts(productsCreated, productsUpdated, variantsCreated, variantsUpdated);
    }

    private static List<ImportIssue> Sorted(List<ImportIssue> issues) =>
        issues.OrderBy(i => i.Line).ThenBy(i => i.Column, StringComparer.Ordinal).Take(500).ToList();

    /// <summary>The downloadable template: our columns and two fictional example products.</summary>
    public static string Template()
    {
        var lines = new List<string>
        {
            Csv.Line(ShopifyCsv.TemplateColumns),
            Csv.Line(["classic-leather-bag", "Classic Leather Bag", "Soft Italian leather, made by hand.", "Almanaam", "Handbags", "Bag", "leather, gift", "active",
                "Colour", "Black", "", "", "", "", "BAG-CL-BLK", "800", "shopify", "12", "deny", "89.00", "119.00", "TRUE", "", "45.00", "EUR"]),
            Csv.Line(["classic-leather-bag", "", "", "", "", "", "", "", "", "Brown", "", "", "", "", "BAG-CL-BRN", "800", "shopify", "4", "deny", "89.00", "", "TRUE", "", "45.00", "EUR"]),
            Csv.Line(["silk-scarf", "Silk Scarf", "", "Almanaam", "Scarves", "Accessory", "silk", "draft",
                "", "", "", "", "", "", "SCARF-01", "100", "", "", "deny", "35.00", "", "TRUE", "", "", "EUR"]),
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

}
