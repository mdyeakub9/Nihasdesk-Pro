using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Media;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Features.Products;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Features.Images;

internal static class ImageErrors
{
    public static readonly Error NoFile = Error.Validation("catalog.image_no_file", "Choose a photo to upload.");

    public static readonly Error TooLarge = Error.Validation("catalog.image_too_large", "The photo is larger than 10 MB or 40 megapixels.");

    /// <summary>Not JPEG, PNG or WebP by its content, or it can't be opened (SEC-04).</summary>
    public static readonly Error TypeNotAllowed = Error.Validation("catalog.image_type_not_allowed", "Upload a JPG, PNG or WebP photo.");

    public static readonly Error TooMany = Error.Validation("catalog.too_many_images", "A product has at most 8 photos.");

    /// <summary>Also for other products' and other workspaces' images (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("catalog.image_not_found", "Photo not found.");

    public static readonly Error OrderMismatch =
        Error.Validation("catalog.image_order_mismatch", "The new order must list every photo of the product once.");
}

/// <summary>The new order of a product's photos: all of them, the main photo first.</summary>
internal sealed record ReorderImagesRequest(List<Guid>? ImageIds);

internal sealed class ReorderImagesValidator : AbstractValidator<ReorderImagesRequest>
{
    public ReorderImagesValidator()
    {
        RuleFor(r => r.ImageIds).NotNull()
            .Must(ids => ids is null || (ids.Count <= ProductImage.MaxPerProduct && ids.Distinct().Count() == ids.Count))
            .WithMessage("At most 8 photos, each once.");
    }
}

/// <summary>A saved photo has links for the dashboard; they need a signed-in team member.</summary>
internal sealed record ProductImageResponse(
    Guid Id,
    int Position,
    string Status,
    string? FailureCode,
    int? Width,
    int? Height,
    string? Url,
    string? ThumbnailUrl);

/// <summary>
/// The product editor's photos (ADR 0031 §7, PRD 4.6). Checked by content and re-encoded during the
/// upload, like the team's photos in the inbox (SEC-04): only pixels are kept, no EXIF or GPS.
/// </summary>
internal sealed class ProductImagesHandler(
    CommerceDbContext db,
    IFileStore files,
    ITenantContext tenant,
    IOptions<ProductImageOptions> options)
{
    public const string Thumbnail = "thumbnail";

    /// <summary>A hard cap at the server, envelope included; the configured limit is checked here.</summary>
    public const long RequestMaxBytes = (10 * 1024 * 1024) + (64 * 1024);

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal) { "image/jpeg", "image/png", "image/webp" };

    public static string UrlOf(Guid productId, Guid imageId) => $"/api/v1/products/{productId}/images/{imageId}";

    public static ProductImageResponse ToResponse(ProductImage image)
    {
        var saved = image.Status == ProductImageStatus.Saved;
        var url = saved ? UrlOf(image.ProductId, image.Id) : null;
        return new ProductImageResponse(
            image.Id,
            image.Position,
            CatalogInput.Text(image.Status),
            image.FailureCode,
            image.Width,
            image.Height,
            url,
            url is null ? null : url + "?size=" + Thumbnail);
    }

    public async Task<List<ProductImage>> OfAsync(Guid productId, CancellationToken ct) =>
        await db.ProductImages.AsNoTracking().Where(i => i.ProductId == productId).OrderBy(i => i.Position).ToListAsync(ct);

    public async Task<Result<ProductImageResponse>> UploadAsync(Guid productId, HttpRequest request, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return CatalogErrors.NotFound;
        }

        if (await db.ProductImages.CountAsync(i => i.ProductId == productId, ct) >= ProductImage.MaxPerProduct)
        {
            return ImageErrors.TooMany;
        }

        var content = await ReadAsync(request, ct);
        if (content.IsFailure)
        {
            return content.Error;
        }

        // The browser's name and type are never believed: only the content decides.
        if (MediaTypes.Detect(content.Value) is not { } detected || !Allowed.Contains(detected.ContentType))
        {
            return ImageErrors.TypeNotAllowed;
        }

        var processed = ImageProcessor.Process(content.Value, options.Value.MaxPixels, ImageProcessor.CardEdge);
        if (processed.Image is not { } image)
        {
            return processed.Failure == ImageFailure.TooManyPixels ? ImageErrors.TooLarge : ImageErrors.TypeNotAllowed;
        }

        var saved = new List<string>();
        try
        {
            var card = await SaveAsync(image.Full, saved, ct);
            var thumbnail = await SaveAsync(image.Thumbnail, saved, ct);
            var aiCopy = await SaveAsync(image.AiCopy, saved, ct);

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Two uploads at once can't both take the 8th place: the product row is locked (RLS still applies).
            await db.Database.ExecuteSqlAsync($"SELECT 1 FROM commerce.products WHERE id = {productId} FOR UPDATE", ct);
            var positions = await db.ProductImages.Where(i => i.ProductId == productId).Select(i => i.Position).ToListAsync(ct);
            if (positions.Count >= ProductImage.MaxPerProduct)
            {
                await DeleteFilesAsync(saved);
                return ImageErrors.TooMany;
            }

            var row = ProductImage.Uploaded(
                tenant.TenantId,
                productId,
                positions.Count == 0 ? 1 : positions.Max() + 1,
                image.ContentType,
                image.Full.Length,
                image.Width,
                image.Height,
                Convert.ToHexStringLower(SHA256.HashData(content.Value)),
                card,
                thumbnail,
                aiCopy);
            db.ProductImages.Add(row);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Result.Success(ToResponse(row));
        }
        catch
        {
            await DeleteFilesAsync(saved);
            throw;
        }
    }

    public async Task<Result<List<ProductImageResponse>>> ReorderAsync(Guid productId, ReorderImagesRequest request, CancellationToken ct)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct))
        {
            return CatalogErrors.NotFound;
        }

        var images = await db.ProductImages.Where(i => i.ProductId == productId).ToListAsync(ct);
        var ids = request.ImageIds!;
        if (ids.Count != images.Count || images.Any(i => !ids.Contains(i.Id)))
        {
            return ImageErrors.OrderMismatch;
        }

        foreach (var image in images)
        {
            image.MoveTo(ids.IndexOf(image.Id) + 1);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(images.OrderBy(i => i.Position).Select(ToResponse).ToList());
    }

    /// <summary>Files first, then the row: a failure leaves a row to delete again, never a forgotten file.</summary>
    public async Task<Result> DeleteAsync(Guid productId, Guid imageId, CancellationToken ct)
    {
        var image = await db.ProductImages.SingleOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId, ct);
        if (image is null)
        {
            return Result.Failure(ImageErrors.NotFound);
        }

        await DeleteFilesAsync([image.CardKey, image.ThumbnailKey, image.AiImageKey]);
        db.ProductImages.Remove(image);
        // The others move up, so the next one becomes the main photo.
        var rest = await db.ProductImages.Where(i => i.ProductId == productId && i.Id != imageId).OrderBy(i => i.Position).ToListAsync(ct);
        for (var i = 0; i < rest.Count; i++)
        {
            rest[i].MoveTo(i + 1);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>The card copy, or the thumbnail; the AI's copy is never served.</summary>
    public async Task<(Stream Content, string ContentType)?> OpenAsync(Guid productId, Guid imageId, bool thumbnail, CancellationToken ct)
    {
        var image = await db.ProductImages.AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == imageId && i.ProductId == productId && i.Status == ProductImageStatus.Saved, ct);
        return image is null ? null : await OpenAsync(files, thumbnail ? image.ThumbnailKey : image.CardKey, image.ContentType, ct);
    }

    internal static async Task<(Stream Content, string ContentType)?> OpenAsync(IFileStore files, string? key, string? contentType, CancellationToken ct)
    {
        if (key is null || contentType is null)
        {
            return null;
        }

        try
        {
            return (await files.OpenReadAsync(key, ct), contentType);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private async Task<Result<byte[]>> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return ImageErrors.NoFile;
        }

        var form = await request.ReadFormAsync(ct);
        if (form.Files.GetFile("file") is not { Length: > 0 } file)
        {
            return ImageErrors.NoFile;
        }

        if (file.Length > options.Value.MaxBytes)
        {
            return ImageErrors.TooLarge;
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return Result.Success(buffer.ToArray());
    }

    private async Task<string> SaveAsync(byte[] content, List<string> saved, CancellationToken ct)
    {
        using var stream = new MemoryStream(content, writable: false);
        var key = await files.SaveAsync(tenant.TenantId, stream, ct);
        saved.Add(key);
        return key;
    }

    private async Task DeleteFilesAsync(IEnumerable<string?> keys)
    {
        foreach (var key in keys.OfType<string>())
        {
            await files.DeleteAsync(key, CancellationToken.None);
        }
    }
}
