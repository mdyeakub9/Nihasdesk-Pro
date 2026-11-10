using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Domain;

/// <summary>Where a product image is (ADR 0031 §7). Only <c>saved</c> images are shown or sent.</summary>
internal enum ProductImageStatus
{
    /// <summary>Waiting for the CSV import's download job.</summary>
    Pending,

    Saved,

    Failed,
}

/// <summary>Why an image couldn't be saved, as stored in <c>failure_code</c>.</summary>
internal static class ProductImageFailure
{
    public const int MaxLength = 30;

    public const string TooLarge = "too_large";

    public const string TypeNotAllowed = "type_not_allowed";

    public const string SourceNotAllowed = "source_not_allowed";

    public const string Network = "network";
}

/// <summary>
/// One picture of a product (ADR 0031 §7, PRD 4.6): checked by content and re-encoded, so only
/// pixels are kept (SEC-04). The lowest position is the main image. Variants point to an image
/// (<see cref="ProductVariant.ImageId"/>), so one picture can serve several sizes of a colour.
/// </summary>
internal sealed class ProductImage : TenantEntity
{
    public const int MaxPerProduct = 8;
    public const int SourceUrlMaxLength = 4096;
    public const int KeyMaxLength = 200;
    public const int ContentTypeMaxLength = 100;

    private ProductImage(Guid tenantId, Guid productId, int position)
        : base(tenantId)
    {
        ProductId = productId;
        Position = position;
    }

    public Guid ProductId { get; private set; }

    /// <summary>1 = main image; the editor's order.</summary>
    public int Position { get; private set; }

    public ProductImageStatus Status { get; private set; }

    public string? FailureCode { get; private set; }

    /// <summary>The shop's public link (CSV import only); also stops a re-import downloading it again.</summary>
    public string? SourceUrl { get; private set; }

    /// <summary>The CSV line the link came from, to report failures per row.</summary>
    public int? SourceLine { get; private set; }

    public Guid? ImportBatchId { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>Of the re-encoded copies (JPEG, or WebP when the picture has transparency).</summary>
    public string? ContentType { get; private set; }

    public long? SizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    /// <summary>Of the file as received; the file itself is never kept.</summary>
    public string? Sha256 { get; private set; }

    /// <summary>At most 1200 px: product cards, the editor and the public link.</summary>
    public string? CardKey { get; private set; }

    public string? ThumbnailKey { get; private set; }

    /// <summary>At most 1024 px, for photo matching (#87). Never served.</summary>
    public string? AiImageKey { get; private set; }

    public static ProductImage Uploaded(
        Guid tenantId,
        Guid productId,
        int position,
        string contentType,
        long sizeBytes,
        int width,
        int height,
        string sha256,
        string cardKey,
        string thumbnailKey,
        string aiImageKey)
    {
        var image = new ProductImage(tenantId, productId, position);
        image.Saved(contentType, sizeBytes, width, height, sha256, cardKey, thumbnailKey, aiImageKey);
        return image;
    }

    public void MoveTo(int position)
    {
        if (position is < 1 or > MaxPerProduct)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        Position = position;
    }

    private void Saved(string contentType, long sizeBytes, int width, int height, string sha256, string cardKey, string thumbnailKey, string aiImageKey)
    {
        Status = ProductImageStatus.Saved;
        FailureCode = null;
        NextAttemptAt = null;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        Sha256 = sha256;
        CardKey = cardKey;
        ThumbnailKey = thumbnailKey;
        AiImageKey = aiImageKey;
    }
}
