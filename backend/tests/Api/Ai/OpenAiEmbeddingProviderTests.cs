using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Nihasdesk.Modules.AiAgent.Llm;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>The embeddings call itself: request, order of results, what is retried (ADR 0026 §2).</summary>
public sealed class OpenAiEmbeddingProviderTests
{
    private const string TwoVectors = """{"data":[{"index":1,"embedding":[0.3,0.4]},{"index":0,"embedding":[0.1,0.2]}]}""";

    [Fact]
    public async Task Sends_the_model_and_key_and_returns_vectors_in_the_order_of_the_texts()
    {
        var handler = new Handler((HttpStatusCode.OK, TwoVectors));

        var vectors = await Provider(handler).EmbedAsync(["uno", "due"], CancellationToken.None);

        Assert.Equal([0.1f, 0.2f], vectors[0]);
        Assert.Equal([0.3f, 0.4f], vectors[1]);
        Assert.Equal("https://api.test/v1/embeddings", handler.Urls.Single());
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Contains("\"model\":\"text-embedding-3-small\"", handler.Bodies.Single(), StringComparison.Ordinal);
        Assert.Contains("\"input\":[\"uno\",\"due\"]", handler.Bodies.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_busy_provider_is_tried_again()
    {
        var handler = new Handler((HttpStatusCode.TooManyRequests, "{}"), (HttpStatusCode.OK, TwoVectors));

        Assert.Equal(2, (await Provider(handler).EmbedAsync(["uno", "due"], CancellationToken.None)).Count);
        Assert.Equal(2, handler.Urls.Count);
    }

    [Fact]
    public async Task A_rejected_key_is_not_tried_again()
    {
        var handler = new Handler((HttpStatusCode.Unauthorized, "{}"));

        var error = await Assert.ThrowsAsync<LlmException>(() => Provider(handler).EmbedAsync(["uno"], CancellationToken.None));

        Assert.Equal("http_401", error.Reason);
        Assert.Single(handler.Urls);
    }

    private static OpenAiEmbeddingProvider Provider(Handler handler)
    {
        var options = new AiOptions();
        options.OpenAi.ApiKey = "test-key";
        return new OpenAiEmbeddingProvider(new HttpClient(handler) { BaseAddress = new Uri("https://api.test/v1/") }, Options.Create(options), TimeProvider.System);
    }

    private sealed class Handler(params (HttpStatusCode Status, string Body)[] answers) : HttpMessageHandler
    {
        private int _next;

        public List<string> Urls { get; } = [];

        public List<string> Bodies { get; } = [];

        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.AbsoluteUri);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Authorization = request.Headers.Authorization?.ToString();
            var (status, body) = answers[Math.Min(_next++, answers.Length - 1)];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
