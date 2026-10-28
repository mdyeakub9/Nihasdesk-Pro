using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Nihasdesk.Modules.Channels.Infrastructure.Meta;

internal sealed record MetaPage(string Id, string Name, string AccessToken, IReadOnlyList<string> Tasks);

/// <summary>A Graph API error. Carries Meta's numeric codes only, never request content.</summary>
internal sealed class MetaGraphException(HttpStatusCode status, int code, int subcode)
    : Exception($"Graph API error {(int)status} (code {code}, subcode {subcode}).")
{
    public HttpStatusCode Status { get; } = status;

    public int Code { get; } = code;

    public int Subcode { get; } = subcode;

    /// <summary>Rate limits, Meta outages and transient errors; everything else won't get better by retrying.</summary>
    public bool IsTransient =>
        Status == HttpStatusCode.TooManyRequests || (int)Status >= 500 || Code is 1 or 2 or 4 or 17 or 32 or 613;
}

/// <summary>
/// Calls to the Graph API. Tokens travel in the Authorization header or form bodies, never in URLs,
/// so they can't end up in request logs (SEC-19). Request logging is off for this client anyway.
/// </summary>
internal sealed class MetaGraphClient(HttpClient http, IOptions<MetaOptions> options)
{
    public const string HttpClientName = "meta-graph";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private MetaOptions Options => options.Value;

    public Uri LoginDialogUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = Options.AppId,
            ["redirect_uri"] = Options.RedirectUri,
            ["state"] = state,
            ["response_type"] = "code",
        };
        if (!string.IsNullOrEmpty(Options.LoginConfigId))
        {
            query["config_id"] = Options.LoginConfigId;
        }
        else
        {
            query["scope"] = "pages_show_list,pages_messaging,pages_manage_metadata,business_management";
        }

        var encoded = string.Join('&', query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return new Uri($"https://www.facebook.com/{Options.GraphApiVersion}/dialog/oauth?{encoded}");
    }

    /// <summary>Code → short-lived user token → long-lived user token, so Page tokens don't expire.</summary>
    public async Task<string> UserTokenAsync(string code, CancellationToken ct)
    {
        var shortLived = await TokenAsync(
            new Dictionary<string, string>
            {
                ["client_id"] = Options.AppId,
                ["client_secret"] = Options.AppSecret,
                ["redirect_uri"] = Options.RedirectUri,
                ["code"] = code,
            },
            ct);

        return await TokenAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "fb_exchange_token",
                ["client_id"] = Options.AppId,
                ["client_secret"] = Options.AppSecret,
                ["fb_exchange_token"] = shortLived,
            },
            ct);
    }

    public async Task<IReadOnlyList<MetaPage>> PagesAsync(string userToken, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, "me/accounts?fields=id,name,access_token,tasks&limit=100", userToken);
        var page = await SendAsync<PageList>(request, ct);
        return page.Data
            .Where(p => p.Id is not null && p.AccessToken is not null)
            .Select(p => new MetaPage(p.Id!, p.Name ?? "Page", p.AccessToken!, p.Tasks ?? []))
            .ToList();
    }

    /// <summary>Lets the app receive the Page's message webhooks.</summary>
    public async Task SubscribeAsync(string pageId, string pageToken, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Post, $"{Segment(pageId)}/subscribed_apps", pageToken);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["subscribed_fields"] = "messages" });
        await SendAsync<JsonElement>(request, ct);
    }

    public async Task UnsubscribeAsync(string pageId, string pageToken, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Delete, $"{Segment(pageId)}/subscribed_apps", pageToken);
        await SendAsync<JsonElement>(request, ct);
    }

    /// <summary>The customer's display name from the User Profile API, or null.</summary>
    public async Task<string?> ProfileNameAsync(string psid, string pageToken, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, $"{Segment(psid)}?fields=first_name,last_name", pageToken);
        var profile = await SendAsync<Profile>(request, ct);
        var name = $"{profile.FirstName} {profile.LastName}".Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>Sends a text reply inside the 24-hour window; returns Meta's message ID.</summary>
    public async Task<string> SendTextAsync(string pageId, string pageToken, string psid, string text, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Post, $"{Segment(pageId)}/messages", pageToken);
        request.Content = JsonContent.Create(
            new { recipient = new { id = psid }, messaging_type = "RESPONSE", message = new { text } });
        var sent = await SendAsync<SendResult>(request, ct);
        return sent.MessageId ?? throw new MetaGraphException(HttpStatusCode.OK, 0, 0);
    }

    private async Task<string> TokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "oauth/access_token") { Content = new FormUrlEncodedContent(form) };
        var token = await SendAsync<TokenResult>(request, ct);
        return token.AccessToken ?? throw new MetaGraphException(HttpStatusCode.OK, 0, 0);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, string accessToken)
    {
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var proof = MetaCrypto.AppSecretProof(accessToken, Options.AppSecret);
        var request = new HttpRequestMessage(method, $"{path}{separator}appsecret_proof={proof}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, ct);
            throw new MetaGraphException(response.StatusCode, error?.Code ?? 0, error?.ErrorSubcode ?? 0);
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new MetaGraphException(response.StatusCode, 0, 0);
    }

    private static async Task<GraphError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return (await response.Content.ReadFromJsonAsync<ErrorEnvelope>(Json, ct))?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Graph IDs are digits; anything else never goes into a URL path.</summary>
    private static string Segment(string id) =>
        id.Length is > 0 and <= 100 && id.All(char.IsAsciiDigit)
            ? id
            : throw new ArgumentException("Invalid Graph ID.", nameof(id));

    private sealed record TokenResult([property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record PageList(List<PageItem> Data);

    private sealed record PageItem(
        string? Id,
        string? Name,
        [property: JsonPropertyName("access_token")] string? AccessToken,
        List<string>? Tasks);

    private sealed record Profile(
        [property: JsonPropertyName("first_name")] string? FirstName,
        [property: JsonPropertyName("last_name")] string? LastName);

    private sealed record SendResult([property: JsonPropertyName("message_id")] string? MessageId);

    private sealed record ErrorEnvelope(GraphError? Error);

    private sealed record GraphError(int Code, [property: JsonPropertyName("error_subcode")] int ErrorSubcode);

    internal static string Describe(MetaGraphException exception) =>
        string.Create(CultureInfo.InvariantCulture, $"meta_{exception.Code}_{exception.Subcode}");
}
