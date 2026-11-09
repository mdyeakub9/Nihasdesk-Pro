using SkiaSharp;

namespace Nihasdesk.Infrastructure.Media;

/// <summary>A clean copy of an image and its smaller sizes; all in <see cref="ContentType"/>, without metadata.</summary>
public sealed record ProcessedImage(byte[] Full, byte[] Thumbnail, byte[] AiCopy, string ContentType, int Width, int Height);

public enum ImageFailure
{
    /// <summary>More pixels than allowed: refused before decoding, against decompression bombs.</summary>
    TooManyPixels,

    /// <summary>Not an image this library can decode, or a damaged one.</summary>
    NotDecodable,
}

public sealed record ImageResult(ProcessedImage? Image, ImageFailure? Failure);

/// <summary>
/// Decodes an image and encodes it again (ADR 0031 §2, SEC-04). Only pixels survive, so EXIF, GPS and
/// anything hidden in the file are gone. The photo's rotation is applied first, as it would be lost too.
/// </summary>
public static class ImageProcessor
{
    public const long DefaultMaxPixels = 40_000_000;
    public const int ThumbnailEdge = 320;
    public const int AiCopyEdge = 1024;

    private const int Quality = 85;

    public static ImageResult Process(byte[] content, long maxPixels = DefaultMaxPixels)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return new(null, ImageFailure.NotDecodable);
        }

        // Only the header has been read so far.
        var encoded = codec.Info;
        if ((long)encoded.Width * encoded.Height > maxPixels)
        {
            return new(null, ImageFailure.TooManyPixels);
        }

        var pixels = new SKImageInfo(encoded.Width, encoded.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        using var source = new SKBitmap(pixels);
        // For a GIF this is the first frame.
        if (codec.GetPixels(pixels, source.GetPixels()) != SKCodecResult.Success)
        {
            return new(null, ImageFailure.NotDecodable);
        }

        var origin = codec.EncodedOrigin;
        var turned = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = turned ? encoded.Height : encoded.Width;
        var height = turned ? encoded.Width : encoded.Height;
        var transparent = encoded.AlphaType != SKAlphaType.Opaque;
        var format = transparent ? SKEncodedImageFormat.Webp : SKEncodedImageFormat.Jpeg;

        byte[] Encode(int edge)
        {
            var scale = Math.Min(1f, (float)edge / Math.Max(width, height));
            return Render(source, origin, width, height, scale, format);
        }

        return new(
            new ProcessedImage(
                Encode(Math.Max(width, height)),
                Encode(ThumbnailEdge),
                Encode(AiCopyEdge),
                transparent ? "image/webp" : "image/jpeg",
                width,
                height),
            null);
    }

    private static byte[] Render(SKBitmap source, SKEncodedOrigin origin, int width, int height, float scale, SKEncodedImageFormat format)
    {
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)),
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb());
        using var target = new SKBitmap(info);
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(format == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
            canvas.Scale((float)info.Width / width, (float)info.Height / height);
            canvas.Concat(Upright(origin, source.Width, source.Height));
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        using var result = SKImage.FromBitmap(target);
        using var encoded = result.Encode(format, Quality);
        return encoded.ToArray();
    }

    /// <summary>Maps stored pixels to how the photo should be seen (EXIF orientation).</summary>
    private static SKMatrix Upright(SKEncodedOrigin origin, float w, float h) => origin switch
    {
        SKEncodedOrigin.TopRight => Matrix(-1, 0, w, 0, 1, 0),
        SKEncodedOrigin.BottomRight => Matrix(-1, 0, w, 0, -1, h),
        SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, h),
        SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
        SKEncodedOrigin.RightTop => Matrix(0, -1, h, 1, 0, 0),
        SKEncodedOrigin.RightBottom => Matrix(0, -1, h, -1, 0, w),
        SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, w),
        _ => SKMatrix.Identity,
    };

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY) =>
        new(scaleX, skewX, transX, skewY, scaleY, transY, 0, 0, 1);
}
