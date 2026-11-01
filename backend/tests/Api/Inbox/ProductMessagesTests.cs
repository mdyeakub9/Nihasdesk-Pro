using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>Send products from the + menu (ADR 0024): catalog prices only, in stock only (CLAUDE.md rule 5, SEC-06, SEC-21, SEC-22).</summary>
[Collection(AuthTests.Name)]
public sealed class ProductMessagesTests(AuthFixture fixture)
{
    private static readonly string[] Options = ["Colore"];

    [Fact]
    public async Task Sends_cards_built_from_the_catalog_and_takes_the_conversation_over()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var bag = await CreateProductAsync(owner, "Borsa nera", ("Nero", 8900, 3), ("Rosso", 9500, 0));
        var scarf = await CreateProductAsync(owner, "Sciarpa", ("Blu", 2500, null));
        var id = (await inbox.ReceiveAsync(workspace, "visitor-p1", "Cosa avete?")).ConversationId;

        // A price or title in the request is ignored: only IDs are read.
        var response = await SendAsync(owner, id, new
        {
            items = new object[]
            {
                new { productId = bag.Id, variantId = bag.Variants[0], price = 1, title = "Fake" },
                new { productId = scarf.Id, variantId = scarf.Variants[0] },
            },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal(2, sent.Count);

        var card = sent[0].GetProperty("product");
        Assert.Equal("Borsa nera", card.GetProperty("title").GetString());
        Assert.Equal("Nero", card.GetProperty("variant").GetString());
        Assert.Equal(8900, card.GetProperty("price").GetInt64());
        Assert.Equal("EUR", card.GetProperty("currency").GetString());
        Assert.StartsWith("🛍️ Borsa nera — Nero\n€89.00 · SKU ", sent[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal("agent", sent[0].GetProperty("author").GetString());

        var history = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Borsa nera", "Sciarpa"], history.Where(m => m.GetProperty("product").ValueKind == JsonValueKind.Object).Select(m => m.GetProperty("product").GetProperty("title").GetString()));
        var conversation = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("human", conversation.GetProperty("responder").GetString());
    }

    [Fact]
    public async Task Out_of_stock_inactive_unknown_and_other_workspaces_products_are_refused_and_nothing_is_sent()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync("Almanaam");
        var (ownerB, workspaceB) = await inbox.OwnerAsync("Bottega Blu");
        var bag = await CreateProductAsync(owner, "Borsa", ("Nero", 8900, 3), ("Rosso", 9500, 0));
        var draft = await CreateProductAsync(owner, "Bozza", status: "draft", variants: ("Unico", 1000, 5));
        var foreign = await CreateProductAsync(ownerB, "Altro", ("Verde", 500, 5));
        var id = (await inbox.ReceiveAsync(workspace, "visitor-p2", "Ciao")).ConversationId;
        var idB = (await inbox.ReceiveAsync(workspaceB, "visitor-p2", "Ciao")).ConversationId;

        var outOfStock = await SendAsync(owner, id, new { items = new[] { new { productId = bag.Id, variantId = bag.Variants[1] } } });
        Assert.Equal(HttpStatusCode.BadRequest, outOfStock.StatusCode);
        Assert.Equal("product.not_available", await AuthFlowTests.CodeOf(outOfStock));

        // One bad item stops the whole send.
        var mixed = await SendAsync(owner, id, new { items = new[] { new { productId = bag.Id, variantId = bag.Variants[0] }, new { productId = draft.Id, variantId = draft.Variants[0] } } });
        Assert.Equal(HttpStatusCode.NotFound, mixed.StatusCode);
        Assert.Equal("product.not_found", await AuthFlowTests.CodeOf(mixed));
        await AssertNotFound(SendAsync(owner, id, new { items = new[] { new { productId = foreign.Id, variantId = foreign.Variants[0] } } }), "product.not_found");
        await AssertNotFound(SendAsync(owner, id, new { items = new[] { new { productId = bag.Id, variantId = foreign.Variants[0] } } }), "product.not_found");
        await AssertNotFound(SendAsync(ownerB, id, new { items = new[] { new { productId = foreign.Id, variantId = foreign.Variants[0] } } }), "conversation.not_found");

        var history = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items");
        Assert.Single(history.EnumerateArray());
        Assert.Single((await ownerB.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{idB}/messages")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Requests_are_validated()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-p3", "Ciao")).ConversationId;
        var six = Enumerable.Range(0, 6).Select(_ => new { productId = Guid.NewGuid(), variantId = Guid.NewGuid() }).ToArray();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, id, new { items = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, id, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, id, new { items = six })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(owner, id, new { items = new[] { new { productId = Guid.Empty, variantId = Guid.NewGuid() } } })).StatusCode);

        using var anonymous = fixture.Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.PostAsJsonAsync($"/api/v1/conversations/{id}/products", new { })).StatusCode);
    }

    internal sealed record CreatedProduct(string Id, List<string> Variants);

    private static async Task<CreatedProduct> CreateProductAsync(ApiClient owner, string title, params (string Option, long Price, int? Stock)[] variants) =>
        await CreateProductAsync(owner, title, "active", variants);

    private static async Task<CreatedProduct> CreateProductAsync(ApiClient owner, string title, string status, params (string Option, long Price, int? Stock)[] variants)
    {
        var created = await owner.PostAsync("/api/v1/products", new
        {
            title,
            status,
            optionNames = Options,
            variants = variants.Select(v => new
            {
                sku = $"S-{Guid.NewGuid():N}"[..12],
                option1 = v.Option,
                price = v.Price,
                // No stock number: not tracked, always available.
                trackInventory = v.Stock is not null,
                stock = v.Stock,
                inventoryPolicy = "deny",
            }).ToArray(),
        });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return new CreatedProduct(
            body.GetProperty("id").GetString()!,
            body.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("id").GetString()!).ToList());
    }

    private static Task<HttpResponseMessage> SendAsync(ApiClient client, Guid id, object body) =>
        client.PostAsync($"/api/v1/conversations/{id}/products", body);

    private static async Task AssertNotFound(Task<HttpResponseMessage> call, string code)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(code, await AuthFlowTests.CodeOf(response));
    }
}
