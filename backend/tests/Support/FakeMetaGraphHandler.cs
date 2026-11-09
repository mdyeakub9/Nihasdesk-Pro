using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Nihasdesk.TestSupport;

/// <summary>A recorded call to the fake Graph API.</summary>
public sealed record GraphCall(HttpMethod Method, string Path, string? Authorization, string Body);

/// <summary>Plays Meta's Graph API for tests (ADR 0017): OAuth, Pages, subscriptions, profiles, sending.</summary>
public sealed class FakeMetaGraphHandler : HttpMessageHandler
{
    public const string AppSecret = "tests-only-meta-app-secret";

    private static readonly string[] PageTasks = ["MESSAGING", "MODERATE"];

    private int _sent;

    public ConcurrentQueue<GraphCall> Calls { get; } = new();

    /// <summary>Pages the signed-in Facebook user grants: (id, name, token).</summary>
    public List<(string Id, string Name, string Token)> Pages { get; } = [("1001", "Bottega Page", "page-token-1001")];

    /// <summary>When set, the Send API answers with this status and Graph error code.</summary>
    public (HttpStatusCode Status, int Code)? SendFailure { get; set; }

    public IReadOnlyList<GraphCall> Sends => Calls.Where(c => c.Method == HttpMethod.Post && c.Path.EndsWith("/messages", StringComparison.Ordinal)).ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content switch
        {
            null => string.Empty,
            // A photo upload: the fields as JSON, the file as its type, size and SHA-256.
            MultipartFormDataContent form => await MultipartAsJsonAsync(form, cancellationToken),
            var content => await content.ReadAsStringAsync(cancellationToken),
        };
        Calls.Enqueue(new GraphCall(request.Method, path, request.Headers.Authorization?.ToString(), body));

        if (path.EndsWith("/oauth/access_token", StringComparison.Ordinal))
        {
            return Json(new { access_token = body.Contains("fb_exchange_token", StringComparison.Ordinal) ? "long-user-token" : "short-user-token" });
        }

        if (path.EndsWith("/me/accounts", StringComparison.Ordinal))
        {
            return Json(new { data = Pages.Select(p => new { id = p.Id, name = p.Name, access_token = p.Token, tasks = PageTasks }) });
        }

        if (path.EndsWith("/subscribed_apps", StringComparison.Ordinal))
        {
            return Json(new { success = true });
        }

        if (path.EndsWith("/messages", StringComparison.Ordinal))
        {
            if (SendFailure is { } failure)
            {
                return Json(new { error = new { code = failure.Code, error_subcode = 0, message = "fake" } }, failure.Status);
            }

            using var sent = JsonDocument.Parse(body);
            return Json(new { recipient_id = sent.RootElement.GetProperty("recipient").GetProperty("id").GetString(), message_id = $"m_out_{Interlocked.Increment(ref _sent)}" });
        }

        // User Profile API: GET /{psid}?fields=first_name,last_name
        return Json(new { first_name = "Giulia", last_name = "Rossi" });
    }

    /// <summary>The last photo sent, as Meta received it.</summary>
    public byte[]? LastUpload { get; private set; }

    private async Task<string> MultipartAsJsonAsync(MultipartFormDataContent form, CancellationToken ct)
    {
        var fields = new Dictionary<string, object?>();
        foreach (var part in form)
        {
            var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty;
            if (part.Headers.ContentDisposition?.FileName is not null)
            {
                var bytes = await part.ReadAsByteArrayAsync(ct);
                LastUpload = bytes;
                fields[name] = new { contentType = part.Headers.ContentType?.MediaType, length = bytes.Length };
            }
            else
            {
                var text = await part.ReadAsStringAsync(ct);
                fields[name] = text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : text;
            }
        }

        return JsonSerializer.Serialize(fields);
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
}
