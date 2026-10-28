using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.Identity.Accounts;

namespace Nihasdesk.Modules.Identity.Infrastructure.Passwords;

internal sealed class BreachedPasswordOptions
{
    public const string SectionName = "Identity:BreachedPasswords";

    public bool Enabled { get; set; } = true;

    public Uri BaseUrl { get; set; } = new("https://api.pwnedpasswords.com/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// ADR 0009: rejects passwords found in Have I Been Pwned. k-anonymity: only the first 5 hex
/// characters of the SHA-1 hash leave the server, with padded responses. If the service is down
/// or slow the password is accepted (fail-open) and a warning without any part of it is logged.
/// </summary>
internal sealed partial class BreachedPasswordValidator(
    IHttpClientFactory httpClients,
    IOptions<BreachedPasswordOptions> options,
    ILogger<BreachedPasswordValidator> logger) : IPasswordValidator<User>
{
    public const string HttpClientName = "hibp";

    public const string ErrorCode = "BreachedPassword";

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        if (!options.Value.Enabled || string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

#pragma warning disable CA5350 // SHA-1 is what the HIBP range API is keyed on; not used for security here.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var prefix = hash[..5];
        var suffix = hash[5..];

        try
        {
            using var timeout = new CancellationTokenSource(options.Value.Timeout);
            var client = httpClients.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.Value.BaseUrl, $"range/{prefix}"));
            request.Headers.Add("Add-Padding", "true");

            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                LogUnavailable(logger, (int)response.StatusCode);
                return IdentityResult.Success;
            }

            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return IsListed(body, suffix)
                ? IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = "This password appears in known data breaches." })
                : IdentityResult.Success;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            LogUnavailable(logger, 0);
            return IdentityResult.Success;
        }
    }

    /// <summary>Lines are <c>SUFFIX:COUNT</c>; padding entries have count 0.</summary>
    private static bool IsListed(string body, string suffix)
    {
        foreach (var line in body.Split('\n'))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator == suffix.Length
                && line.AsSpan(0, separator).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan(separator + 1).Trim(), out var count)
                && count > 0)
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Breached-password check unavailable (status {Status}); password accepted (fail-open)")]
    private static partial void LogUnavailable(ILogger logger, int status);
}
