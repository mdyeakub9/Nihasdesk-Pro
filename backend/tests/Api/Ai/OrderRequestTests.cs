using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Inbox;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>The AI takes orders as requests a person approves (PRD 4.9, ADR 0025, CLAUDE.md rule 5, SEC-36, SEC-38, SEC-43).</summary>
[Collection(AuthTests.Name)]
public sealed class OrderRequestTests(AuthFixture fixture)
{
    private static readonly string[] Colour = ["Colore"];

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task The_ai_quotes_then_creates_a_pending_request_and_states_only_the_quoted_total()
    {
        var shop = await ShopAsync();
        var quoteMarker = Marker();
        var confirmMarker = Marker();
        Api.Ai.Respond = call =>
            Mine(call, confirmMarker)
                ? call.ToolResults.Count == 0
                    ? FakeAiAnswer.Call("create_order_request", Request(shop))
                    : FakeAiAnswer.Say("Grazie Giulia! Richiesta ricevuta, totale €185.00 alla consegna. Il negozio la confermerà a breve.")
                : Mine(call, quoteMarker)
                    ? call.ToolResults.Count == 0
                        ? FakeAiAnswer.Call("quote_order", Quote(shop))
                        : FakeAiAnswer.Say("Riepilogo: 2 borse €178.00 + spedizione €7.00 = €185.00. Confermi?")
                    : FakeAiAnswer.Say("ok");

        var id = await ReceiveAsync(shop, $"Vorrei 2 borse nere, Giulia Rossi via Roma 12 07100 Sassari tel +39 333 1234567 {quoteMarker}");
        await RunAiUntilAsync(quoteMarker);
        var quoted = Api.Ai.Calls.Last(c => Mine(c, quoteMarker)).ToolResults.Single();
        Assert.Contains("185.00 EUR", quoted, StringComparison.Ordinal);
        Assert.Empty((await Orders(shop.Owner)).EnumerateArray());

        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await shop.Inbox.ReceiveAsync(shop.Workspace, shop.Visitor, $"Sì, confermo {confirmMarker}");
        await RunAiUntilAsync(confirmMarker);

        var created = Api.Ai.Calls.Last(c => Mine(c, confirmMarker)).ToolResults.Single();
        Assert.Contains("\"orderNumber\":1001", created, StringComparison.Ordinal);
        var order = Assert.Single((await Orders(shop.Owner)).EnumerateArray());
        Assert.Equal("pending", order.GetProperty("status").GetString());
        Assert.Equal("ai", order.GetProperty("source").GetString());
        Assert.Equal(18500, order.GetProperty("total").GetInt64());
        var detail = await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/orders/{order.GetProperty("id").GetString()}");
        Assert.Equal(id.ToString(), detail.GetProperty("conversationId").GetString());
        // Nothing is taken until a person approves (CLAUDE.md rule 5).
        Assert.Equal(5, await StockAsync(shop));
        Assert.Contains(await AiTextsAsync(shop.Owner, id), t => t.Contains("€185.00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_total_the_tools_did_not_return_is_held_back()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok")
            : call.ToolResults.Count == 0 ? FakeAiAnswer.Call("quote_order", Quote(shop))
            : FakeAiAnswer.Say("Totale scontato: solo €150.00!");

        var id = await ReceiveAsync(shop, $"Quanto costano 2 borse? {marker}");
        await RunAiUntilAsync(marker);

        Assert.DoesNotContain(await AiTextsAsync(shop.Owner, id), t => t.Contains("150", StringComparison.Ordinal));
        Assert.Equal("human", (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation").GetProperty("responder").GetString());
    }

    [Fact]
    public async Task Invalid_details_are_refused_by_code_and_a_second_pending_request_is_not_created()
    {
        var shop = await ShopAsync();
        var bad = Marker();
        var first = Marker();
        var second = Marker();
        Api.Ai.Respond = call =>
            call.ToolResults.Count > 0 ? FakeAiAnswer.Say("Ok.")
            // Newest marker first: the history also holds the earlier ones.
            : Mine(call, second) || Mine(call, first) ? FakeAiAnswer.Call("create_order_request", Request(shop))
            : Mine(call, bad) ? FakeAiAnswer.Call("create_order_request", Request(shop, phone: "SECRET-not-a-phone", postalCode: "71"))
            : FakeAiAnswer.Say("ok");

        await ReceiveAsync(shop, $"Ordino {bad}");
        await RunAiUntilAsync(bad);
        var refused = Api.Ai.Calls.Last(c => Mine(c, bad)).ToolResults.Single();
        Assert.Contains("Phone", refused, StringComparison.Ordinal);
        Assert.Contains("PostalCode", refused, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", refused, StringComparison.Ordinal);
        Assert.Empty((await Orders(shop.Owner)).EnumerateArray());

        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await shop.Inbox.ReceiveAsync(shop.Workspace, shop.Visitor, $"Riprovo {first}");
        await RunAiUntilAsync(first);
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await shop.Inbox.ReceiveAsync(shop.Workspace, shop.Visitor, $"E ancora {second}");
        await RunAiUntilAsync(second);

        Assert.Single((await Orders(shop.Owner)).EnumerateArray());
        Assert.Contains("already has an order request", Api.Ai.Calls.Last(c => Mine(c, second)).ToolResults.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approving_tells_the_customer_in_the_chat_and_rejecting_keeps_the_reason_for_the_team()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok")
            : call.ToolResults.Count == 0 ? FakeAiAnswer.Call("create_order_request", Request(shop))
            : FakeAiAnswer.Say("Il negozio confermerà a breve.");
        var id = await ReceiveAsync(shop, $"Confermo l'ordine {marker}");
        await RunAiUntilAsync(marker);
        var orderId = (await Orders(shop.Owner))[0].GetProperty("id").GetString();

        await shop.Owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.OK, (await shop.Owner.Http.PostAsync($"/api/v1/orders/{orderId}/approve", null)).StatusCode);
        var messages = await MessagesAsync(shop.Owner, id);
        var confirmation = messages.Last();
        Assert.Equal("agent", confirmation.GetProperty("author").GetString());
        Assert.False(confirmation.GetProperty("internal").GetBoolean());
        Assert.Equal(
            "Ordine #1001 confermato ✅\n\n2× Borsa nera — Nero  €178.00\nSpedizione (Sardegna)  €7.00\nTotale da pagare alla consegna: €185.00\nConsegna: Via Roma 12, 07100 Sassari\n\nGrazie!",
            confirmation.GetProperty("text").GetString());
        Assert.Equal(3, await StockAsync(shop));
        // Telling the customer doesn't take the chat away from the AI.
        Assert.Equal("ai", (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation").GetProperty("responder").GetString());

        // A second request in the same chat, rejected.
        var again = Marker();
        Api.Ai.Respond = call => !Mine(call, again) ? FakeAiAnswer.Say("ok")
            : call.ToolResults.Count == 0 ? FakeAiAnswer.Call("create_order_request", Request(shop, quantity: 1))
            : FakeAiAnswer.Say("Ricevuto.");
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await shop.Inbox.ReceiveAsync(shop.Workspace, shop.Visitor, $"Un'altra {again}");
        await RunAiUntilAsync(again);
        var pending = (await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=pending")).GetProperty("items")[0].GetProperty("id").GetString();
        await shop.Owner.RefreshCsrfAsync();
        await shop.Owner.Http.PostAsJsonAsync($"/api/v1/orders/{pending}/reject", new { reason = "SECRET-REASON cliente noto" });

        var rejection = (await MessagesAsync(shop.Owner, id)).Last();
        Assert.Contains("non possiamo confermare l'ordine #1002", rejection.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(await MessagesAsync(shop.Owner, id), m => m.GetProperty("text").GetString()!.Contains("SECRET-REASON", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Outside_the_24_hour_window_the_customer_is_not_written_to_and_the_team_gets_a_note()
    {
        var shop = await ShopAsync();
        Guid id;
        Guid orderId;
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(shop.Workspace));
            // A Messenger customer who last wrote 25 hours ago (the window rule is Meta's, SEC-43).
            var received = await scope.ServiceProvider.GetRequiredService<IInboxIngest>().ReceiveAsync(
                new InboundMessage("messenger", shop.Visitor, "Giulia Rossi", "Ordine", null, Api.Clock.GetUtcNow().AddHours(-25)), CancellationToken.None);
            id = received.ConversationId;
            var created = await scope.ServiceProvider.GetRequiredService<IOrderRequests>().CreateAsync(
                id,
                [new OrderRequestLine(Guid.Parse(shop.ProductId), Guid.Parse(shop.VariantId), 1)],
                new OrderRequestDetails("Giulia Rossi", "+39 333 1234567", "Via Roma 12", "07100", "Sassari", "SS", "IT", Guid.Parse(shop.ZoneId), null),
                CancellationToken.None);
            orderId = created.Value.OrderId;
        }

        var before = (await MessagesAsync(shop.Owner, id)).Count;
        await shop.Owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.OK, (await shop.Owner.Http.PostAsync($"/api/v1/orders/{orderId}/approve", null)).StatusCode);

        var note = Assert.Single((await MessagesAsync(shop.Owner, id)).Skip(before));
        Assert.True(note.GetProperty("internal").GetBoolean());
        Assert.Contains("24-hour window is closed", note.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_team_assistant_and_another_workspace_cannot_create_requests()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok")
            : call.ToolResults.Count == 0 ? FakeAiAnswer.Call("create_order_request", Request(shop))
            : FakeAiAnswer.Say("REPLY: Ok.");
        var id = await ReceiveAsync(shop, $"Ordino {marker}");

        Assert.Equal(HttpStatusCode.OK, (await shop.Owner.PostAsync($"/api/v1/conversations/{id}/assist", new { })).StatusCode);
        var call = Api.Ai.Calls.Last(c => Mine(c, marker));
        Assert.DoesNotContain("create_order_request", call.Tools);
        Assert.Contains("Unknown tool", call.ToolResults.Single(), StringComparison.Ordinal);
        Assert.Empty((await Orders(shop.Owner)).EnumerateArray());

        // Another workspace's products and zone are not found by the tool.
        var other = await ShopAsync("Bottega Blu");
        var foreign = Marker();
        Api.Ai.Respond = c => !Mine(c, foreign) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("create_order_request", Request(shop))
            : FakeAiAnswer.Say("Ok.");
        await ReceiveAsync(other, $"Ordino {foreign}");
        await RunAiUntilAsync(foreign);
        Assert.Contains("not valid", Api.Ai.Calls.Last(c => Mine(c, foreign)).ToolResults.Single(), StringComparison.Ordinal);
        Assert.Empty((await Orders(other.Owner)).EnumerateArray());
        Assert.Empty((await Orders(shop.Owner)).EnumerateArray());
    }

    private sealed record Shop(ApiClient Owner, string Workspace, InboxTests Inbox, string ProductId, string VariantId, string ZoneId, string Visitor);

    private async Task<Shop> ShopAsync(string business = "Bottega Verde")
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync(business);
        var product = await (await owner.PostAsync("/api/v1/products", new
        {
            title = "Borsa nera",
            status = "active",
            optionNames = Colour,
            variants = new object[] { new { sku = $"R-{Guid.NewGuid():N}"[..12], option1 = "Nero", price = 8900, trackInventory = true, stock = 5, inventoryPolicy = "deny" } },
        })).Content.ReadFromJsonAsync<JsonElement>();
        await owner.RefreshCsrfAsync();
        var zones = await (await owner.Http.PutAsJsonAsync("/api/v1/shipping/zones", new
        {
            zones = new[] { new { name = "Sardegna", areas = "SS", price = 700, currency = "EUR", freeOver = (long?)null, minDays = 2, maxDays = 4 } },
        })).Content.ReadFromJsonAsync<JsonElement>();
        return new Shop(owner, workspace, inbox, product.GetProperty("id").GetString()!, product.GetProperty("variants")[0].GetProperty("id").GetString()!,
            zones[0].GetProperty("id").GetString()!, "visitor-" + Guid.NewGuid().ToString("N")[..8]);
    }

    private static string Quote(Shop s) =>
        JsonSerializer.Serialize(new { items = new[] { new { product_id = s.ProductId, variant_id = s.VariantId, quantity = 2 } }, zone_id = s.ZoneId });

    private static string Request(Shop s, string phone = "+39 333 1234567", string postalCode = "07100", int quantity = 2) =>
        JsonSerializer.Serialize(new
        {
            items = new[] { new { product_id = s.ProductId, variant_id = s.VariantId, quantity } },
            zone_id = s.ZoneId,
            customer_name = "Giulia Rossi",
            phone,
            street = "Via Roma 12",
            postal_code = postalCode,
            city = "Sassari",
            province = "SS",
            country = "IT",
        });

    private static async Task<Guid> ReceiveAsync(Shop shop, string text) =>
        (await shop.Inbox.ReceiveAsync(shop.Workspace, shop.Visitor, text, name: "Giulia Rossi")).ConversationId;

    /// <summary>Earlier tests leave other workspaces' conversations due too; run until this marker was answered.</summary>
    private async Task RunAiUntilAsync(string marker)
    {
        for (var i = 0; i < 10 && !Api.Ai.Calls.Any(c => Mine(c, marker)); i++)
        {
            await Api.RunAiAsync();
        }
    }

    private static string Marker() => "#" + Guid.NewGuid().ToString("N")[..8];

    private static bool Mine(FakeAiCall call, string marker) =>
        call.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);

    private static async Task<JsonElement> Orders(ApiClient owner) =>
        (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders")).GetProperty("items");

    private static async Task<int> StockAsync(Shop shop) =>
        (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{shop.ProductId}")).GetProperty("variants")[0].GetProperty("stock").GetInt32();

    private static async Task<List<JsonElement>> MessagesAsync(ApiClient owner, Guid id) =>
        (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages?limit=100")).GetProperty("items").EnumerateArray().ToList();

    private static async Task<List<string>> AiTextsAsync(ApiClient owner, Guid id) =>
        (await MessagesAsync(owner, id))
            .Where(m => m.GetProperty("author").GetString() == "ai" && !m.GetProperty("internal").GetBoolean())
            .Select(m => m.GetProperty("text").GetString()!)
            .ToList();
}
