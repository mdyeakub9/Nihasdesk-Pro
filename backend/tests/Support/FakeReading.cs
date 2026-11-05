using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.TestSupport;

/// <summary>Vectors of the right size without a provider. Counts what was embedded, and can fail.</summary>
internal sealed class FakeEmbeddings : IEmbeddingProvider
{
    private int _calls;

    public const int Dimensions = 1536;

    public ConcurrentQueue<string> Embedded { get; } = new();

    /// <summary>Set to make every call fail, like a provider that is down.</summary>
    public string? FailWith { get; set; }

    public bool IsConfigured { get; set; } = true;

    public string Model => "fake-embedding";

    public int Calls => _calls;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        if (FailWith is { } reason)
        {
            throw new LlmException(reason);
        }

        foreach (var text in texts)
        {
            Embedded.Enqueue(text);
        }

        return Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(t =>
        {
            var vector = new float[Dimensions];
            vector[Math.Abs(t.GetHashCode(StringComparison.Ordinal)) % Dimensions] = 1;
            return vector;
        })]);
    }
}

/// <summary>Answers by URL path. Unknown pages are 404; a test can also make the site fail.</summary>
public sealed class FakeWebHandler : HttpMessageHandler
{
    public ConcurrentDictionary<string, (string ContentType, byte[] Body)> Pages { get; } = new();

    public HttpStatusCode? FailWith { get; set; }

    /// <summary>URL to where it redirects.</summary>
    public ConcurrentDictionary<string, string> Redirects { get; } = new();

    public ConcurrentQueue<string> Requested { get; } = new();

    public void Page(string url, string html) => Pages[url] = ("text/html", Encoding.UTF8.GetBytes(html));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requested.Enqueue(request.RequestUri!.AbsoluteUri);
        if (Redirects.TryGetValue(request.RequestUri.AbsoluteUri, out var target))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(target) } });
        }

        if (FailWith is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }

        if (!Pages.TryGetValue(request.RequestUri!.AbsoluteUri, out var page))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        var content = new ByteArrayContent(page.Body);
        content.Headers.TryAddWithoutValidation("Content-Type", page.ContentType);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
