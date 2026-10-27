using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nihasdesk.Infrastructure.Security;

namespace Nihasdesk.TestSupport;

/// <summary>Talks to the API like the dashboard: fetches a CSRF token and sends it on every change.</summary>
public sealed partial class ApiClient(HttpClient http) : IDisposable
{
    public HttpClient Http { get; } = http;

    /// <summary>Gets a fresh token; needed again after signing in or out because tokens are bound to the user.</summary>
    public async Task RefreshCsrfAsync()
    {
        var response = await Http.GetFromJsonAsync<JsonElement>("/api/v1/csrf");
        Http.DefaultRequestHeaders.Remove(ApiSecurityExtensions.CsrfHeaderName);
        Http.DefaultRequestHeaders.Add(ApiSecurityExtensions.CsrfHeaderName, response.GetProperty("token").GetString());
    }

    public async Task<HttpResponseMessage> PostAsync(string path, object? body = null)
    {
        await RefreshCsrfAsync();
        return await Http.PostAsJsonAsync(path, body ?? new { });
    }

    public Task<HttpResponseMessage> SignupAsync(string email, string password = DefaultPassword, string business = "Almanaam", string language = "en") =>
        PostAsync("/api/v1/auth/signup", new { name = "Test Owner", email, password, businessName = business, language });

    public Task<HttpResponseMessage> LoginAsync(string email, string password = DefaultPassword, bool rememberMe = false) =>
        PostAsync("/api/v1/auth/login", new { email, password, rememberMe });

    /// <summary>Signs up, opens the verification link from the captured email and returns the response.</summary>
    public async Task<HttpResponseMessage> SignupAndVerifyAsync(FakeEmailSender emails, string email, string business = "Almanaam")
    {
        ArgumentNullException.ThrowIfNull(emails);
        (await SignupAsync(email, business: business)).EnsureSuccessStatusCode();
        var (userId, token) = ParseVerifyLink(emails.LastTo(email).TextBody);
        return await PostAsync("/api/v1/auth/verify-email", new { userId, token });
    }

    public static (Guid UserId, string Token) ParseVerifyLink(string body)
    {
        var match = VerifyLink().Match(body);
        if (!match.Success)
        {
            throw new InvalidOperationException("No verification link in the email.");
        }

        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    public const string DefaultPassword = "correct horse battery staple";

    public void Dispose() => Http.Dispose();

    [GeneratedRegex(@"/verify-email\?user=(?<user>[0-9a-f-]{36})&token=(?<token>[^\s""]+)")]
    private static partial Regex VerifyLink();
}
