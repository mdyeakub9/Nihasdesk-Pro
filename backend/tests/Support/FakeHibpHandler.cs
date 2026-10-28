using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Nihasdesk.TestSupport;

/// <summary>Stands in for the HIBP range API, so tests never call the internet.</summary>
public sealed class FakeHibpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, byte> _breached = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    /// <summary>When true, every request fails like an outage.</summary>
    public bool Unavailable { get; set; }

    public IReadOnlyList<HttpRequestMessage> Requests => [.. _requests];

    public void MarkBreached(string password) => _breached[Sha1(password)] = 0;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Enqueue(request);
        if (Unavailable)
        {
            throw new HttpRequestException("HIBP unavailable (test)");
        }

        var prefix = request.RequestUri!.AbsolutePath.Split('/')[^1];
        var body = new StringBuilder("0000000000000000000000000000000000A:0\n");
        foreach (var hash in _breached.Keys.Where(h => h.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            body.Append(hash[5..]).Append(":42\n");
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToString()) });
    }

#pragma warning disable CA5350 // Mirrors the HIBP API, which is keyed on SHA-1.
    private static string Sha1(string value) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value)));
#pragma warning restore CA5350
}
