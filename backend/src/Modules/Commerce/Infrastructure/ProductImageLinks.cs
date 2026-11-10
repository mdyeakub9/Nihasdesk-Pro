using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Media;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Infrastructure;

/// <summary>Product image limits and public links (ADR 0031 §7); Phase 1 defaults of PRD 4.6.</summary>
internal sealed class ProductImageOptions
{
    public const string SectionName = "Commerce:Images";

    public const int MinKeyBytes = 32;

    public long MaxBytes { get; set; } = 10 * 1024 * 1024;

    public long MaxPixels { get; set; } = ImageProcessor.DefaultMaxPixels;

    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Base64, at least 32 random bytes; the same value for API and Workers. A secret (CLAUDE.md rule 6).</summary>
    public string? SigningKey { get; set; }

    /// <summary>From <c>App:PublicUrl</c>: where Meta can reach the API (locally the ngrok domain).</summary>
    public string? PublicUrl { get; set; }

    public static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return true;
        }

        var bytes = new byte[key.Length];
        return Convert.TryFromBase64String(key, bytes, out var length) && length >= MinKeyBytes;
    }
}

/// <summary>
/// Signs and checks the token of <c>/media/p/{token}</c>: version, workspace, image and expiry, with
/// an HMAC-SHA256 over a purpose string, so a token made for anything else never fits. The link
/// says which workspace's image it is, but only because we signed it: nothing a stranger can change.
/// </summary>
internal sealed class ProductImageSigner(IOptions<ProductImageOptions> options)
{
    public const string PathPrefix = "/media/p/";

    /// <summary>Longest token accepted; the real ones are 99 characters.</summary>
    public const int MaxTokenLength = 128;

    private const byte Version = 1;
    private const int PayloadLength = 1 + 16 + 16 + 8;
    private const int MacLength = 32;

    private static readonly byte[] Purpose = Encoding.ASCII.GetBytes("nihasdesk/product-image/v1\0");

    private readonly byte[]? _key = string.IsNullOrEmpty(options.Value.SigningKey) ? null : Convert.FromBase64String(options.Value.SigningKey);

    private readonly Uri? _base = Uri.TryCreate(options.Value.PublicUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps ? url : null;

    /// <summary>Public links need both a key and an https address Meta can reach.</summary>
    public bool Enabled => _key is not null && _base is not null;

    public string? Url(Guid tenantId, Guid imageId, DateTimeOffset expires) =>
        Enabled ? new Uri(_base!, PathPrefix + Sign(tenantId, imageId, expires)).ToString() : null;

    public string Sign(Guid tenantId, Guid imageId, DateTimeOffset expires)
    {
        var key = _key ?? throw new InvalidOperationException("No signing key for product image links.");
        Span<byte> token = stackalloc byte[PayloadLength + MacLength];
        token[0] = Version;
        tenantId.TryWriteBytes(token[1..17], bigEndian: true, out _);
        imageId.TryWriteBytes(token[17..33], bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(token[33..PayloadLength], expires.ToUnixTimeSeconds());
        Mac(key, token[..PayloadLength], token[PayloadLength..]);
        return Base64Url.EncodeToString(token);
    }

    /// <summary>False for anything not made by <see cref="Sign"/> with our key, changed in any way, or expired.</summary>
    public bool TryVerify(string? token, DateTimeOffset now, out Guid tenantId, out Guid imageId)
    {
        tenantId = Guid.Empty;
        imageId = Guid.Empty;
        if (_key is null || string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[MaxTokenLength];
        if (Base64Url.DecodeFromChars(token, bytes, out var consumed, out var written) != System.Buffers.OperationStatus.Done
            || consumed != token.Length
            || written != PayloadLength + MacLength
            || bytes[0] != Version)
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[MacLength];
        Mac(_key, bytes[..PayloadLength], expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, bytes[PayloadLength..written])
            || BinaryPrimitives.ReadInt64BigEndian(bytes[33..PayloadLength]) < now.ToUnixTimeSeconds())
        {
            return false;
        }

        tenantId = new Guid(bytes[1..17], bigEndian: true);
        imageId = new Guid(bytes[17..33], bigEndian: true);
        return tenantId != Guid.Empty;
    }

    private static void Mac(byte[] key, ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        Span<byte> message = stackalloc byte[Purpose.Length + PayloadLength];
        Purpose.CopyTo(message);
        payload.CopyTo(message[Purpose.Length..]);
        HMACSHA256.HashData(key, message, destination);
    }
}

/// <summary>Links for the current workspace's images; checks the image is saved, so no link points to nothing.</summary>
internal sealed class ProductImageLinks(
    ProductImageSigner signer,
    CommerceDbContext db,
    ITenantContext tenant,
    IOptions<ProductImageOptions> options,
    TimeProvider time) : IProductImageLinks
{
    public async Task<string?> CardUrlAsync(Guid imageId, CancellationToken cancellationToken)
    {
        // The tenant-scoped context: another workspace's image is simply not there (SEC-06).
        if (!signer.Enabled || !await db.ProductImages.AnyAsync(i => i.Id == imageId && i.Status == ProductImageStatus.Saved, cancellationToken))
        {
            return null;
        }

        return signer.Url(tenant.TenantId, imageId, time.GetUtcNow() + options.Value.LinkLifetime);
    }
}
