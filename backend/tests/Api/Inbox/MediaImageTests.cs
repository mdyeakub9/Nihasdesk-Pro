using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nihasdesk.Infrastructure.Media;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Channels;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Inbox;
using Nihasdesk.Modules.Inbox.Contracts;
using SkiaSharp;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>What a file is and what stays of an image (ADR 0031 §2, SEC-04).</summary>
public sealed class MediaImageTests
{
    public static byte[] Png(int width, int height, SKColor? color = null) => Encoded(width, height, SKEncodedImageFormat.Png, color ?? SKColors.Red);

    public static byte[] Jpeg(int width, int height) => Encoded(width, height, SKEncodedImageFormat.Jpeg, SKColors.Blue);

    /// <summary>A JPEG with an EXIF block: rotated 90° for viewing, plus a text standing in for GPS data.</summary>
    public static byte[] JpegWithExif(int width, int height, string marker)
    {
        var jpeg = Jpeg(width, height);
        // TIFF (little-endian) with one tag: orientation (0x0112) = 6.
        byte[] tiff = [0x49, 0x49, 0x2A, 0x00, 0x08, 0, 0, 0, 0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0, 0, 0, 0x06, 0x00, 0, 0, 0, 0, 0, 0];
        byte[] payload = [.. "Exif\0\0"u8, .. tiff, .. Encoding.ASCII.GetBytes(marker)];
        var length = payload.Length + 2;
        return [jpeg[0], jpeg[1], 0xFF, 0xE1, (byte)(length >> 8), (byte)length, .. payload, .. jpeg[2..]];
    }

    /// <summary>The other tests run the job inside the API, so only this one proves the Workers host can start it.</summary>
    [Fact]
    public void The_workers_host_has_everything_the_download_job_needs()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.AppConnectionName}"] = "Host=localhost;Database=none",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        new InboxModule().AddWorkerServices(services, configuration);
        new ChannelsModule().AddWorkerServices(services, configuration);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.Contains(provider.GetServices<IHostedService>(), s => s.GetType().Name == "MediaDownloadProcessor");
        Assert.Contains(scope.ServiceProvider.GetServices<IChannelMediaFetcher>(), f => f.Channel == ChannelKind.Messenger);
    }

    [Fact]
    public void Metadata_is_removed_and_the_photo_stays_the_right_way_up()
    {
        var photo = JpegWithExif(40, 20, "GPS-45.4642N-9.1900E");
        Assert.Contains("GPS-45.4642N", Encoding.ASCII.GetString(photo), StringComparison.Ordinal);

        var image = ImageProcessor.Process(photo).Image!;

        Assert.Equal((20, 40), (image.Width, image.Height));
        Assert.Equal("image/jpeg", image.ContentType);
        foreach (var copy in new[] { image.Full, image.Thumbnail, image.AiCopy })
        {
            var text = Encoding.ASCII.GetString(copy);
            Assert.DoesNotContain("GPS-45.4642N", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Exif", text, StringComparison.Ordinal);
            using var codec = SKCodec.Create(new MemoryStream(copy));
            Assert.Equal(SKEncodedOrigin.TopLeft, codec.EncodedOrigin);
        }
    }

    [Fact]
    public void Smaller_copies_keep_the_shape_and_small_images_are_not_blown_up()
    {
        var big = ImageProcessor.Process(Png(2000, 1000, new SKColor(10, 20, 30))).Image!;
        using (var thumbnail = SKBitmap.Decode(big.Thumbnail))
        using (var aiCopy = SKBitmap.Decode(big.AiCopy))
        {
            Assert.Equal((320, 160), (thumbnail.Width, thumbnail.Height));
            Assert.Equal((1024, 512), (aiCopy.Width, aiCopy.Height));
        }

        var small = ImageProcessor.Process(Png(50, 30)).Image!;
        using var unchanged = SKBitmap.Decode(small.Thumbnail);
        Assert.Equal((50, 30), (unchanged.Width, unchanged.Height));
    }

    [Fact]
    public void Too_many_pixels_are_refused_before_decoding()
    {
        var result = ImageProcessor.Process(Png(100, 100), maxPixels: 9_999);

        Assert.Null(result.Image);
        Assert.Equal(ImageFailure.TooManyPixels, result.Failure);
    }

    [Fact]
    public void A_transparent_image_keeps_its_transparency()
    {
        var image = ImageProcessor.Process(Png(10, 10, SKColors.Transparent)).Image!;

        Assert.Equal("image/webp", image.ContentType);
    }

    [Fact]
    public void Something_that_only_starts_like_an_image_is_not_decodable()
    {
        byte[] fake = [0xFF, 0xD8, 0xFF, 0xE0, .. Encoding.ASCII.GetBytes("this is not a photo at all")];

        var result = ImageProcessor.Process(fake);

        Assert.Null(result.Image);
        Assert.Equal(ImageFailure.NotDecodable, result.Failure);
    }

    [Theory]
    [InlineData("FFD8FFE0", "image/jpeg")]
    [InlineData("89504E470D0A1A0A", "image/png")]
    [InlineData("474946383961", "image/gif")]
    [InlineData("524946460000000057454250", "image/webp")]
    [InlineData("255044462D", "application/pdf")]
    [InlineData("4F676753", "audio/ogg")]
    [InlineData("494433", "audio/mpeg")]
    [InlineData("FFFB", "audio/mpeg")]
    [InlineData("FFF1", "audio/aac")]
    [InlineData("0000001866747970" + "4D344120", "audio/mp4")]
    [InlineData("0000001866747970" + "69736F6D", "video/mp4")]
    [InlineData("0000001866747970" + "68656963", "image/heic")]
    public void Allowed_types_are_known_by_their_content(string hex, string contentType) =>
        Assert.Equal(contentType, MediaTypes.Detect(Convert.FromHexString(hex))?.ContentType);

    [Theory]
    [InlineData("4D5A90000300")] // Windows program
    [InlineData("7F454C46")] // Linux program
    [InlineData("504B0304")] // ZIP
    [InlineData("3C68746D6C3E")] // <html>
    [InlineData("3C737667")] // <svg
    [InlineData("0000001866747970" + "71742020")] // MP4 family, brand we don't allow
    [InlineData("")]
    public void Everything_else_is_unknown(string hex) => Assert.Null(MediaTypes.Detect(Convert.FromHexString(hex)));

    private static byte[] Encoded(int width, int height, SKEncodedImageFormat format, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
