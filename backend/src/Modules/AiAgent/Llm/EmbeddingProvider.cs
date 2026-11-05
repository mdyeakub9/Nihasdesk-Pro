using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Nihasdesk.Modules.AiAgent.Llm;

/// <summary>Turns text into vectors for search by meaning (ADR 0026 §2); the logic never knows the vendor.</summary>
internal interface IEmbeddingProvider
{
    /// <summary>False when no API key is configured: sections are then found by keywords only.</summary>
    bool IsConfigured { get; }

    /// <summary>Stored per section, so a model change can re-embed later.</summary>
    string Model { get; }

    /// <summary>One vector per text, in order. Throws <see cref="LlmException"/> when the provider fails.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

/// <summary>OpenAI embeddings. Same key and endpoint as replies (ADR 0028); request logging is off (SEC-19).</summary>
internal sealed class OpenAiEmbeddingProvider(HttpClient http, IOptions<AiOptions> options, TimeProvider time) : IEmbeddingProvider
{
    public const string HttpClientName = "openai-embeddings";
    public const int MaxAttempts = 3;

    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.OpenAi.ApiKey);

    public string Model => options.Value.EmbeddingModel;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        for (var attempt = 1; ; attempt++)
        {
            string reason;
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "embeddings")
                {
                    Content = JsonContent.Create(new { model = Model, input = texts }),
                };
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.OpenAi.ApiKey);
                using var response = await http.SendAsync(message, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                    var data = json.RootElement.GetProperty("data");
                    var vectors = new float[data.GetArrayLength()][];
                    foreach (var item in data.EnumerateArray())
                    {
                        vectors[item.GetProperty("index").GetInt32()] = [.. item.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle())];
                    }

                    return vectors;
                }

                reason = $"http_{(int)response.StatusCode}";
                // A rejected key or request won't get better by trying again.
                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                {
                    throw new LlmException(reason);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                reason = ex.GetType().Name;
            }

            if (attempt >= MaxAttempts)
            {
                throw new LlmException(reason);
            }

            await Task.Delay(TimeSpan.FromSeconds(1 << attempt), time, cancellationToken);
        }
    }
}
