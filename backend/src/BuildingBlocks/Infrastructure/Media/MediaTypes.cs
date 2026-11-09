using System.Text;

namespace Nihasdesk.Infrastructure.Media;

/// <summary>What a file is, going by its content. MP4 can hold sound only, so the channel's word decides that.</summary>
public enum MediaFamily
{
    Image,
    Audio,
    Mp4,
    Pdf,
}

public sealed record DetectedMedia(MediaFamily Family, string ContentType, string Extension);

/// <summary>
/// Works out a file's type from its first bytes, never from its name or a header (SEC-04). Anything
/// not on this list, such as a program renamed .jpg, gets null.
/// </summary>
public static class MediaTypes
{
    // HEIC and its relatives. "mif1" also covers AVIF: both only count if they can be decoded.
    private static readonly string[] HeicBrands = ["heic", "heix", "hevc", "hevx", "heim", "heis", "mif1", "msf1"];

    private static readonly string[] Mp4Brands = ["isom", "iso2", "iso4", "iso5", "iso6", "mp41", "mp42", "avc1", "dash", "M4V ", "MSNV"];

    private static readonly string[] Mp4AudioBrands = ["M4A ", "M4B "];

    public static DetectedMedia? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return new(MediaFamily.Image, "image/jpeg", ".jpg");
        }

        if (content.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return new(MediaFamily.Image, "image/png", ".png");
        }

        if (content.StartsWith("GIF87a"u8) || content.StartsWith("GIF89a"u8))
        {
            return new(MediaFamily.Image, "image/gif", ".gif");
        }

        if (content.Length >= 12 && content.StartsWith("RIFF"u8) && content[8..12].SequenceEqual("WEBP"u8))
        {
            return new(MediaFamily.Image, "image/webp", ".webp");
        }

        if (content.StartsWith("%PDF-"u8))
        {
            return new(MediaFamily.Pdf, "application/pdf", ".pdf");
        }

        if (content.StartsWith("OggS"u8))
        {
            return new(MediaFamily.Audio, "audio/ogg", ".ogg");
        }

        if (content.StartsWith("ID3"u8))
        {
            return new(MediaFamily.Audio, "audio/mpeg", ".mp3");
        }

        if (content.Length >= 12 && content[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = Encoding.ASCII.GetString(content[8..12]);
            if (HeicBrands.Contains(brand))
            {
                return new(MediaFamily.Image, "image/heic", ".heic");
            }

            if (Mp4AudioBrands.Contains(brand))
            {
                return new(MediaFamily.Audio, "audio/mp4", ".m4a");
            }

            return Mp4Brands.Contains(brand) ? new(MediaFamily.Mp4, "video/mp4", ".mp4") : null;
        }

        if (content.Length >= 2 && content[0] == 0xFF)
        {
            // Sync bits with layer 0: AAC in ADTS. With a real layer: MP3 without an ID3 tag.
            if ((content[1] & 0xF6) == 0xF0)
            {
                return new(MediaFamily.Audio, "audio/aac", ".aac");
            }

            if ((content[1] & 0xE0) == 0xE0 && (content[1] & 0x06) != 0)
            {
                return new(MediaFamily.Audio, "audio/mpeg", ".mp3");
            }
        }

        return null;
    }
}
