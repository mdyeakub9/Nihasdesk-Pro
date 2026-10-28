using System.Security.Cryptography;
using System.Text;

namespace Nihasdesk.Modules.Channels.Infrastructure.Meta;

/// <summary>
/// The Meta app (section <c>Meta</c>). Secrets come from user secrets locally and the secret store
/// in deployment, never from the repo (CLAUDE.md rule 6, SEC-13).
/// </summary>
internal sealed class MetaOptions
{
    public const string SectionName = "Meta";

    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Our own random value; Meta echoes it when the webhook is set up.</summary>
    public string WebhookVerifyToken { get; set; } = string.Empty;

    public string GraphApiVersion { get; set; } = "v24.0";

    /// <summary>Facebook Login for Business configuration; when empty the classic scopes are requested.</summary>
    public string? LoginConfigId { get; set; }

    /// <summary>Defaults to <c>{App:DashboardUrl}/api/v1/channels/meta/callback</c>; must be registered in the Meta app.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>From <c>App:DashboardUrl</c>: where the browser goes after connecting.</summary>
    public string DashboardUrl { get; set; } = string.Empty;

    public bool IsConfigured => AppId.Length > 0 && AppSecret.Length > 0;
}

/// <summary>Background processing of webhooks and replies (section <c>Channels</c>).</summary>
internal sealed class ChannelsOptions
{
    public const string SectionName = "Channels";

    /// <summary>Off in tests, which run the processors step by step.</summary>
    public bool BackgroundProcessing { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int BatchSize { get; set; } = 20;
}

internal static class MetaCrypto
{
    /// <summary>
    /// Checks <c>X-Hub-Signature-256: sha256=&lt;hex&gt;</c>, the HMAC-SHA256 of the raw body with the
    /// app secret, in constant time (SEC-17).
    /// </summary>
    public static bool IsValidSignature(ReadOnlySpan<byte> body, string? header, string appSecret)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrEmpty(appSecret)
            || header is null
            || !header.StartsWith(prefix, StringComparison.Ordinal)
            || header.Length != prefix.Length + 64)
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(header.AsSpan(prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary><c>appsecret_proof</c> for Graph calls: proves the token is used by our server.</summary>
    public static string AppSecretProof(string accessToken, string appSecret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(accessToken)));

    public static bool FixedTimeEquals(string? a, string b) =>
        a is not null && b.Length > 0
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
