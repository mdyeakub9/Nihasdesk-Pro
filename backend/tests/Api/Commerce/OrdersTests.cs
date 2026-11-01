using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Api.Tests.Inbox;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.TestSupport;
using Npgsql;

namespace Nihasdesk.Api.Tests.Commerce;

/// <summary>Orders and order requests (PRD 4.9, ADR 0025): prices and stock by code, AI only requests (CLAUDE.md rule 5, SEC-06, SEC-11, SEC-41).</summary>
[Collection(AuthTests.Name)]
public sealed class OrdersTests(AuthFixture fixture)
{
    private const string Path = "/api/v1/orders";
    private static readonly string[] Colour = ["Colore"];

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task A_team_order_is_priced_by_the_server_confirmed_numbered_and_takes_the_stock()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var bag = await ProductAsync(owner, "Borsa nera", 8900, stock: 5);
        var zone = await ZoneAsync(owner, price: 700, freeOver: 15000);
        var conversation = (await inbox.ReceiveAsync(workspace, "visitor-o1", "Vorrei la borsa")).ConversationId;

        // Prices, totals or a status in the request are not read (SEC-22, SEC-41).
        var created = await owner.PostAsync(Path, Order(zone, (bag, 1), conversation, extra: new { price = 1, total = 1, status = "rejected" }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1001, order.GetProperty("number").GetInt32());
        Assert.Equal("confirmed", order.GetProperty("status").GetString());
        Assert.Equal("team", order.GetProperty("source").GetString());
        Assert.Equal(8900, order.GetProperty("subtotal").GetInt64());
        Assert.Equal(700, order.GetProperty("shippingCost").GetInt64());
        Assert.Equal(9600, order.GetProperty("total").GetInt64());
        Assert.Equal("cod", order.GetProperty("paymentMethod").GetString());
        Assert.Equal("07100", order.GetProperty("postalCode").GetString());
        Assert.Equal(conversation.ToString(), order.GetProperty("conversationId").GetString());
        Assert.Equal(4, await StockAsync(owner, bag));

        // Two bags: over the free-shipping threshold.
        var second = await (await owner.PostAsync(Path, Order(zone, (bag, 2)))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1002, second.GetProperty("number").GetInt32());
        Assert.Equal(0, second.GetProperty("shippingCost").GetInt64());
        Assert.Equal(17800, second.GetProperty("total").GetInt64());
        Assert.Equal(2, await StockAsync(owner, bag));

        var tooMany = await owner.PostAsync(Path, Order(zone, (bag, 3)));
        Assert.Equal(HttpStatusCode.Conflict, tooMany.StatusCode);
        Assert.Equal("product.not_available", await AuthFlowTests.CodeOf(tooMany));
        Assert.Equal(2, await StockAsync(owner, bag));

        var list = await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}?status=confirmed");
        Assert.Equal([1002, 1001], list.GetProperty("items").EnumerateArray().Select(o => o.GetProperty("number").GetInt32()));
    }

    [Fact]
    public async Task An_ai_request_waits_for_a_person_and_approval_takes_the_stock_once()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var bag = await ProductAsync(owner, "Borsa", 8900, stock: 3);
        var zone = await ZoneAsync(owner, price: 700);
        var conversation = (await inbox.ReceiveAsync(workspace, "visitor-o2", "La prendo")).ConversationId;

        var request = await RequestAsync(workspace, conversation, bag, 2, zone);
        Assert.True(request.IsSuccess);
        Assert.Equal(18500, request.Value.Total);
        var id = request.Value.OrderId;
        var pending = await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}");
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.Equal("ai", pending.GetProperty("source").GetString());
        // Nothing is taken until a person approves (CLAUDE.md rule 5).
        Assert.Equal(3, await StockAsync(owner, bag));

        var approved = await PostAsync(owner, $"{Path}/{id}/approve");
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal("confirmed", (await approved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(1, await StockAsync(owner, bag));

        var again = await PostAsync(owner, $"{Path}/{id}/approve");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("order.not_pending", await AuthFlowTests.CodeOf(again));
        Assert.Equal(1, await StockAsync(owner, bag));
    }

    [Fact]
    public async Task Concurrent_approvals_never_oversell()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var (member, _) = await new InboxNotesTests(fixture).MemberOfAsync(owner);
        var bag = await ProductAsync(owner, "Ultima borsa", 8900, stock: 1);
        var zone = await ZoneAsync(owner, price: 700);
        var first = (await inbox.ReceiveAsync(workspace, "visitor-o3a", "Io")).ConversationId;
        var second = (await inbox.ReceiveAsync(workspace, "visitor-o3b", "Anche io")).ConversationId;
        var a = (await RequestAsync(workspace, first, bag, 1, zone)).Value.OrderId;
        var b = (await RequestAsync(workspace, second, bag, 1, zone)).Value.OrderId;

        await owner.RefreshCsrfAsync();
        await member.RefreshCsrfAsync();
        var results = await Task.WhenAll(owner.Http.PostAsync($"{Path}/{a}/approve", null), member.Http.PostAsync($"{Path}/{b}/approve", null));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var refused = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("product.not_available", await AuthFlowTests.CodeOf(refused));
        Assert.Equal(0, await StockAsync(owner, bag));
    }

    [Fact]
    public async Task A_pending_request_can_be_edited_priced_again_and_rejected_with_a_reason()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var bag = await ProductAsync(owner, "Borsa", 8900, stock: 10);
        var scarf = await ProductAsync(owner, "Sciarpa", 2500, stock: null);
        var zone = await ZoneAsync(owner, price: 700);
        var conversation = (await inbox.ReceiveAsync(workspace, "visitor-o4", "Ordine")).ConversationId;
        var id = (await RequestAsync(workspace, conversation, bag, 1, zone)).Value.OrderId;

        await owner.RefreshCsrfAsync();
        var edited = await owner.Http.PutAsJsonAsync($"{Path}/{id}", Order(zone, (scarf, 2), conversation));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var body = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pending", body.GetProperty("status").GetString());
        Assert.Equal("Sciarpa", Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("title").GetString());
        Assert.Equal(5700, body.GetProperty("total").GetInt64());

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(owner, $"{Path}/{id}/reject", new { reason = " " })).StatusCode);
        var rejected = await PostAsync(owner, $"{Path}/{id}/reject", new { reason = "Indirizzo fuori zona" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var final = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", final.GetProperty("status").GetString());
        Assert.Equal("Indirizzo fuori zona", final.GetProperty("rejectReason").GetString());
        Assert.Equal(10, await StockAsync(owner, bag));

        await owner.RefreshCsrfAsync();
        var late = await owner.Http.PutAsJsonAsync($"{Path}/{id}", Order(zone, (bag, 1)));
        Assert.Equal("order.not_pending", await AuthFlowTests.CodeOf(late));
    }

    [Fact]
    public async Task Details_are_validated_by_code_and_the_ai_is_told_only_which_fields_to_ask_again()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var bag = await ProductAsync(owner, "Borsa", 8900, stock: 5);
        var zone = await ZoneAsync(owner, price: 700);
        var conversation = (await inbox.ReceiveAsync(workspace, "visitor-o5", "Ciao")).ConversationId;

        foreach (var bad in new[]
        {
            Order(zone, (bag, 1), extra: new { phone = "call me" }),
            Order(zone, (bag, 1), extra: new { postalCode = "7100" }),
            Order(zone, (bag, 1), extra: new { country = "Italy" }),
            Order(zone, (bag, 0)),
            Order(zone, (bag, 100)),
            Order(Guid.Empty.ToString(), (bag, 1)),
        })
        {
            var response = await owner.PostAsync(Path, bad);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(bad)} → {response.StatusCode}");
        }

        var unknownZone = await owner.PostAsync(Path, Order(Guid.NewGuid().ToString(), (bag, 1)));
        Assert.Equal("order.zone_not_found", await AuthFlowTests.CodeOf(unknownZone));

        await using var scope = Scope(workspace);
        var invalid = await scope.ServiceProvider.GetRequiredService<IOrderRequests>().CreateAsync(
            conversation,
            [new OrderRequestLine(Guid.Parse(bag.Id), Guid.Parse(bag.VariantId), 1)],
            new OrderRequestDetails("Giulia Rossi", "SECRET-PHONE", "Via Roma 1", "SECRET-CAP", "Sassari", "SS", "IT", Guid.Parse(zone), null),
            CancellationToken.None);
        Assert.False(invalid.IsSuccess);
        Assert.Equal("order.invalid", invalid.Error.Code);
        Assert.Contains("Phone", invalid.Error.Message, StringComparison.Ordinal);
        Assert.Contains("PostalCode", invalid.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", invalid.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_quote_prices_a_draft_without_saving_or_taking_stock_and_counts_and_chat_filter_work()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var (ownerB, _) = await inbox.OwnerAsync("Bottega Blu");
        var bag = await ProductAsync(owner, "Borsa", 8900, stock: 2);
        var zone = await ZoneAsync(owner, price: 700, freeOver: 10000);
        var chat = (await inbox.ReceiveAsync(workspace, "visitor-o7", "Ciao")).ConversationId;
        var other = (await inbox.ReceiveAsync(workspace, "visitor-o8", "Ciao")).ConversationId;

        var quote = await PostAsync(owner, $"{Path}/quote", new { shippingZoneId = zone, items = new[] { new { productId = bag.Id, variantId = bag.VariantId, quantity = 2, unitPrice = 1 } } });
        Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        var body = await quote.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(17800, body.GetProperty("subtotal").GetInt64());
        Assert.Equal(0, body.GetProperty("shippingCost").GetInt64());
        Assert.Equal(8900, body.GetProperty("items")[0].GetProperty("unitPrice").GetInt64());
        Assert.Equal(2, await StockAsync(owner, bag));
        Assert.Empty((await owner.Http.GetFromJsonAsync<JsonElement>(Path)).GetProperty("items").EnumerateArray());
        Assert.Equal("product.not_available", await AuthFlowTests.CodeOf(await PostAsync(owner, $"{Path}/quote", new { shippingZoneId = zone, items = new[] { new { productId = bag.Id, variantId = bag.VariantId, quantity = 3 } } })));
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(owner, $"{Path}/quote", new { shippingZoneId = zone })).StatusCode);
        await AssertNotFound(PostAsync(ownerB, $"{Path}/quote", new { shippingZoneId = zone, items = new[] { new { productId = bag.Id, variantId = bag.VariantId, quantity = 1 } } }), "product.not_found");

        await owner.PostAsync(Path, Order(zone, (bag, 1), chat));
        await RequestAsync(workspace, other, bag, 1, zone);
        var counts = await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}/counts");
        Assert.Equal(1, counts.GetProperty("pending").GetInt32());
        Assert.Equal(1, counts.GetProperty("confirmed").GetInt32());
        Assert.Equal(0, counts.GetProperty("rejected").GetInt32());
        Assert.Equal(0, (await ownerB.Http.GetFromJsonAsync<JsonElement>($"{Path}/counts")).GetProperty("pending").GetInt32());

        var forChat = (await owner.Http.GetFromJsonAsync<JsonElement>($"{Path}?conversationId={chat}")).GetProperty("items");
        Assert.Equal("confirmed", Assert.Single(forChat.EnumerateArray()).GetProperty("status").GetString());
        Assert.Empty((await ownerB.Http.GetFromJsonAsync<JsonElement>($"{Path}?conversationId={chat}")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Another_workspace_gets_not_found_and_the_database_hides_its_orders()
    {
        var inbox = new InboxTests(fixture);
        var (ownerA, workspaceA) = await inbox.OwnerAsync("Almanaam");
        var (ownerB, workspaceB) = await inbox.OwnerAsync("Bottega Blu");
        var bag = await ProductAsync(ownerA, "Borsa", 8900, stock: 5);
        var zone = await ZoneAsync(ownerA, price: 700);
        var bagB = await ProductAsync(ownerB, "Altro", 1000, stock: 5);
        var zoneB = await ZoneAsync(ownerB, price: 500);
        var conversationA = (await inbox.ReceiveAsync(workspaceA, "visitor-o6", "Ciao")).ConversationId;
        var id = (await RequestAsync(workspaceA, conversationA, bag, 1, zone)).Value.OrderId;

        Assert.Empty((await ownerB.Http.GetFromJsonAsync<JsonElement>(Path)).GetProperty("items").EnumerateArray());
        await AssertNotFound(ownerB.Http.GetAsync($"{Path}/{id}"), "order.not_found");
        await AssertNotFound(PostAsync(ownerB, $"{Path}/{id}/approve"), "order.not_found");
        await AssertNotFound(PostAsync(ownerB, $"{Path}/{id}/reject", new { reason = "x" }), "order.not_found");
        await ownerB.RefreshCsrfAsync();
        await AssertNotFound(ownerB.Http.PutAsJsonAsync($"{Path}/{id}", Order(zoneB, (bagB, 1))), "order.not_found");
        // A's products, zone and conversation can't be used in B's orders.
        await AssertNotFound(ownerB.PostAsync(Path, Order(zoneB, (bag, 1))), "product.not_found");
        Assert.Equal("order.zone_not_found", await AuthFlowTests.CodeOf(await ownerB.PostAsync(Path, Order(zone, (bagB, 1)))));
        await AssertNotFound(ownerB.PostAsync(Path, Order(zoneB, (bagB, 1), conversationA)), "conversation.not_found");
        await using (var scopeB = Scope(workspaceB))
        {
            var foreign = await scopeB.ServiceProvider.GetRequiredService<IOrderRequests>().CreateAsync(
                conversationA, [new OrderRequestLine(Guid.Parse(bagB.Id), Guid.Parse(bagB.VariantId), 1)], Details(zoneB), CancellationToken.None);
            Assert.Equal("conversation.not_found", foreign.Error.Code);
        }

        Assert.Equal("pending", (await ownerA.Http.GetFromJsonAsync<JsonElement>($"{Path}/{id}")).GetProperty("status").GetString());

        await using var connection = new NpgsqlConnection(fixture.Database.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @b, false)", connection))
        {
            setTenant.Parameters.AddWithValue("b", workspaceB);
            await setTenant.ExecuteNonQueryAsync();
        }

        foreach (var table in new[] { "commerce.orders", "commerce.order_items" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE tenant_id = @a", connection);
            count.Parameters.AddWithValue("a", Guid.Parse(workspaceA));
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }

        await using var update = new NpgsqlCommand("UPDATE commerce.orders SET status = 'confirmed' WHERE id = @id", connection);
        update.Parameters.AddWithValue("id", id);
        Assert.Equal(0, await update.ExecuteNonQueryAsync());

        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync(Path)).StatusCode);
    }

    internal sealed record CreatedProduct(string Id, string VariantId);

    private static Dictionary<string, object?> Order(string zone, (CreatedProduct Product, int Quantity) line, Guid? conversationId = null, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["conversationId"] = conversationId,
            ["customerName"] = "Giulia Rossi",
            ["phone"] = "+39 333 123 4567",
            ["street"] = "Via Roma 12",
            ["postalCode"] = "07100",
            ["city"] = "Sassari",
            ["province"] = "SS",
            ["country"] = "it",
            ["shippingZoneId"] = zone,
            ["notes"] = null,
            ["items"] = new[] { new { productId = line.Product.Id, variantId = line.Product.VariantId, quantity = line.Quantity } },
        };
        if (extra is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
            {
                body[property.Name] = property.Value;
            }
        }

        return body;
    }

    private static OrderRequestDetails Details(string zone) =>
        new("Giulia Rossi", "+39 333 123 4567", "Via Roma 12", "07100", "Sassari", "SS", "IT", Guid.Parse(zone), null);

    private async Task<NihasdeskResult> RequestAsync(string workspace, Guid conversation, CreatedProduct product, int quantity, string zone)
    {
        await using var scope = Scope(workspace);
        var result = await scope.ServiceProvider.GetRequiredService<IOrderRequests>().CreateAsync(
            conversation, [new OrderRequestLine(Guid.Parse(product.Id), Guid.Parse(product.VariantId), quantity)], Details(zone), CancellationToken.None);
        return new NihasdeskResult(result.IsSuccess, result.IsSuccess ? result.Value : null!);
    }

    private sealed record NihasdeskResult(bool IsSuccess, OrderRequestCreated Value);

    private AsyncServiceScope Scope(string workspace)
    {
        var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        return scope;
    }

    private static async Task<CreatedProduct> ProductAsync(ApiClient owner, string title, long price, int? stock)
    {
        var created = await owner.PostAsync("/api/v1/products", new
        {
            title,
            status = "active",
            optionNames = Colour,
            variants = new object[] { new { sku = $"O-{Guid.NewGuid():N}"[..12], option1 = "Nero", price, trackInventory = stock is not null, stock, inventoryPolicy = "deny" } },
        });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return new CreatedProduct(body.GetProperty("id").GetString()!, body.GetProperty("variants")[0].GetProperty("id").GetString()!);
    }

    private static async Task<int?> StockAsync(ApiClient owner, CreatedProduct product)
    {
        var body = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{product.Id}");
        var stock = body.GetProperty("variants")[0].GetProperty("stock");
        return stock.ValueKind == JsonValueKind.Null ? null : stock.GetInt32();
    }

    private static async Task<string> ZoneAsync(ApiClient owner, long price, long? freeOver = null)
    {
        await owner.RefreshCsrfAsync();
        var response = await owner.Http.PutAsJsonAsync("/api/v1/shipping/zones", new
        {
            zones = new[] { new { name = "Sardegna", areas = "SS, CA", price, currency = "EUR", freeOver, minDays = 2, maxDays = 4 } },
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>())[0].GetProperty("id").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostAsync(ApiClient client, string path, object? body = null)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PostAsJsonAsync(path, body ?? new { });
    }

    private static async Task AssertNotFound(Task<HttpResponseMessage> call, string code)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(code, await AuthFlowTests.CodeOf(response));
    }
}
