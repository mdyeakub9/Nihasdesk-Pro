using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Nihasdesk.Modules.Identity.Accounts;

namespace Nihasdesk.Modules.Identity.Infrastructure.Mfa;

/// <summary>
/// Between a correct password and the MFA code (ADR 0015): a 5-minute, encrypted, HttpOnly cookie
/// that names the user and "remember me". No session exists until the code is verified. The
/// security stamp is included, so a password change in between invalidates it.
/// </summary>
internal sealed class MfaChallenge(IDataProtectionProvider dataProtection, TimeProvider time)
{
    public const string CookieName = "__Host-nd_mfa";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly IDataProtector _protector = dataProtection.CreateProtector("Nihasdesk.Identity.MfaChallenge");

    public void Issue(HttpResponse response, User user, bool remember)
    {
        var expires = time.GetUtcNow() + Lifetime;
        var payload = $"{user.Id:N}|{(remember ? 1 : 0)}|{expires.ToUnixTimeSeconds()}|{user.SecurityStamp}";
        response.Cookies.Append(CookieName, _protector.Protect(payload), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            MaxAge = Lifetime,
        });
    }

    public Pending? Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var value))
        {
            return null;
        }

        try
        {
            var parts = _protector.Unprotect(value).Split('|');
            var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
            return parts.Length == 4 && time.GetUtcNow() < expires
                ? new Pending(Guid.ParseExact(parts[0], "N"), parts[1] == "1", parts[3])
                : null;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(CookieName, new CookieOptions { Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });

    internal sealed record Pending(Guid UserId, bool Remember, string SecurityStamp);
}
