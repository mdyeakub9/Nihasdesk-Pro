using System.Security.Cryptography;
using System.Text;

namespace Nihasdesk.Modules.Identity.Infrastructure.Invitations;

/// <summary>Invitation tokens: 256-bit random, URL-safe; the database keeps only the SHA-256 hash.</summary>
internal static class InvitationTokens
{
    public static (string Token, byte[] Hash) Create()
    {
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return (token, Hash(token));
    }

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
