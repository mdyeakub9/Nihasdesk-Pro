using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Nihasdesk.TestSupport;

/// <summary>RFC 6238 TOTP (SHA-1, 30 s, 6 digits), like an authenticator app.</summary>
public static class Totp
{
    public static string Code(string base32Secret, DateTimeOffset at)
    {
        var counter = at.ToUnixTimeSeconds() / 30;
        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, counter);
#pragma warning disable CA5350 // RFC 6238 default; matches authenticator apps.
        var hash = HMACSHA1.HashData(FromBase32(base32Secret), message);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7FFFFFFF;
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = input.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }

        return [.. bytes];
    }
}
