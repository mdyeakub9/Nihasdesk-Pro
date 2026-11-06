using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Evals;

/// <summary>
/// The made-up shop the eval set is written for (<c>docs/evals/test-shop.json</c>), in the API's own
/// request shapes: products, shipping zones, policies and FAQ answers. No real people or shops.
/// </summary>
internal sealed class TestShop(JsonElement root, string json)
{
    public string Name => root.GetProperty("name").GetString()!;

    /// <summary>The whole file, given to the judge as the shop's facts.</summary>
    public string Json => json;

    public static TestShop Load(string path)
    {
        var text = File.ReadAllText(path);
        using var document = JsonDocument.Parse(text);
        return new TestShop(document.RootElement.Clone(), text);
    }

    /// <summary>Every amount the shop itself states, in cents: prices, shipping costs, thresholds.</summary>
    public IReadOnlySet<long> Prices()
    {
        var prices = new HashSet<long>();
        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name is "price" or "compareAtPrice" or "freeOver" && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out var cents))
                        {
                            prices.Add(cents);
                        }

                        Walk(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
                case JsonValueKind.String:
                    foreach (var cents in Nihasdesk.Modules.AiAgent.Agent.Guardrails.Amounts(element.GetString()))
                    {
                        prices.Add(cents);
                    }

                    break;
            }
        }

        Walk(root);
        return prices;
    }

    /// <summary>Creates the shop in the signed-in owner's workspace through the API, like an owner would.</summary>
    public async Task SeedAsync(ApiClient owner)
    {
        foreach (var product in root.GetProperty("products").EnumerateArray())
        {
            await SendAsync(owner, HttpMethod.Post, "/api/v1/products", product);
        }

        await SendAsync(owner, HttpMethod.Put, "/api/v1/shipping/zones", root.GetProperty("shipping"));
        await SendAsync(owner, HttpMethod.Put, "/api/v1/ai/policies", root.GetProperty("policies"));
        foreach (var answer in root.GetProperty("answers").EnumerateArray())
        {
            await SendAsync(owner, HttpMethod.Post, "/api/v1/ai/answers", answer);
        }
    }

    /// <summary>Uploads the shop's document; the caller reads it like the Workers job, then calls <see cref="UseDocumentAsync"/>.</summary>
    public static async Task<Guid> UploadDocumentAsync(ApiClient owner, string path)
    {
        await owner.RefreshCsrfAsync();
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(File.ReadAllBytes(path));
        form.Add(file, "file", Path.GetFileName(path));
        using var response = await owner.Http.PostAsync("/api/v1/ai/sources/files", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public static async Task UseDocumentAsync(ApiClient owner, Guid id)
    {
        using var response = await owner.PostAsync($"/api/v1/ai/sources/{id}/use");
        response.EnsureSuccessStatusCode();
    }

    private static async Task SendAsync(ApiClient client, HttpMethod method, string path, JsonElement body)
    {
        await client.RefreshCsrfAsync();
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await client.Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Seeding {method} {path} failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }
    }
}
