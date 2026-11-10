using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Channels;

/// <summary>
/// Rich messages on Messenger (Issue #91, ADR 0032): carousels built from the catalog at send time,
/// opaque button tokens, taps as customer messages, and the text fallback (rule 5, SEC-36, SEC-41, SEC-43).
/// </summary>
[Collection(AuthTests.Name)]
public sealed class RichMessagesTests(AuthFixture fixture)
{
    private const string Webhook = "/api/v1/webhooks/meta";
    private static readonly string[] Options = ["Colore"];

    private NihasdeskApiFactory Api => fixture.Api;

    private string OwnerEmail { get; set; } = string.Empty;

    [Fact]
    public async Task Send_products_goes_out_as_one_carousel_with_catalog_prices_and_opaque_buttons()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var (id, _) = await ConversationAsync(owner, pageId);
        var bag = await CreateProductAsync(owner, "Borsa Milano", ("Nero", 8900));
        var scarf = await CreateProductAsync(owner, "Sciarpa", ("Blu", 2500), ("Rosso", 3000));

        // A price or title in the request is ignored: only ids are read (SEC-22).
        var sent = await owner.PostAsync($"/api/v1/conversations/{id}/products", new
        {
            items = new object[] { new { productId = bag.Id, variantId = bag.Variants[0], price = 1, title = "Fake" }, new { productId = scarf.Id } },
        });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        var message = Assert.Single((await sent.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal("carousel", message.GetProperty("richKind").GetString());
        Assert.Equal("pending", message.GetProperty("delivery").GetString());

        Assert.Equal(1, await Api.SendRepliesAsync());
        using var body = JsonDocument.Parse(Api.Meta.Sends[^1].Body);
        var payload = body.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("payload");
        Assert.Equal("generic", payload.GetProperty("template_type").GetString());
        var elements = payload.GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(2, elements.Count);
        Assert.Equal("Borsa Milano", elements[0].GetProperty("title").GetString());
        Assert.Equal("€89.00 · Nero", elements[0].GetProperty("subtitle").GetString());
        Assert.Equal("da €25.00", elements[1].GetProperty("subtitle").GetString());
        var buttons = elements[0].GetProperty("buttons").EnumerateArray().ToList();
        Assert.Equal(["🛒 Lo voglio", "📏 Taglie e colori", "🔍 Simili"], buttons.Select(b => b.GetProperty("title").GetString()));
        Assert.All(buttons, b =>
        {
            Assert.Equal("postback", b.GetProperty("type").GetString());
            // No ids, prices or names in what Meta stores (ADR 0032 §3).
            Assert.Matches("^rm1:[A-Za-z0-9_-]{22}$", b.GetProperty("payload").GetString());
        });
        Assert.DoesNotContain(bag.Id, Api.Meta.Sends[^1].Body, StringComparison.OrdinalIgnoreCase);

        // The team sees what the customer saw, without the payloads.
        var shown = await LastMessageAsync(owner, id);
        Assert.Equal("sent", shown.GetProperty("delivery").GetString());
        Assert.False(shown.GetProperty("fallbackText").GetBoolean());
        var card = shown.GetProperty("rich").GetProperty("cards")[0];
        Assert.Equal(8900, card.GetProperty("price").GetInt64());
        Assert.Equal("buy", card.GetProperty("buttons")[0].GetProperty("action").GetString());
        Assert.DoesNotContain("rm1:", shown.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cards_show_the_variant_photo_else_the_main_photo_through_a_signed_link()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var (id, _) = await ConversationAsync(owner, pageId);
        var bag = await CreateProductAsync(owner, "Borsa Milano", ("Nero", 8900), ("Rosso", 8900));
        var scarf = await CreateProductAsync(owner, "Sciarpa", ("Blu", 2500));
        var main = await PhotoAsync(owner, bag.Id, SkiaSharp.SKColors.Black);
        var red = await PhotoAsync(owner, bag.Id, SkiaSharp.SKColors.Red);
        var current = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{bag.Id}");
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.GetRawText())!;
        var variants = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(body["variants"].GetRawText())!;
        variants[1]["imageId"] = JsonSerializer.SerializeToElement(red);
        body["variants"] = JsonSerializer.SerializeToElement(variants);
        await owner.RefreshCsrfAsync();
        (await owner.Http.PutAsJsonAsync($"/api/v1/products/{bag.Id}", body)).EnsureSuccessStatusCode();

        await owner.PostAsync($"/api/v1/conversations/{id}/products", new
        {
            items = new object[] { new { productId = bag.Id, variantId = bag.Variants[0] }, new { productId = bag.Id, variantId = bag.Variants[1] }, new { productId = scarf.Id } },
        });
        Assert.Equal(1, await Api.SendRepliesAsync());

        using var sent = JsonDocument.Parse(Api.Meta.Sends[^1].Body);
        var elements = sent.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("elements").EnumerateArray().ToList();
        var black = elements[0].GetProperty("image_url").GetString()!;
        var redLink = elements[1].GetProperty("image_url").GetString()!;
        Assert.StartsWith(NihasdeskApiFactory.PublicUrl + "/media/p/", black, StringComparison.Ordinal);
        // No photo, no image: Meta never gets a broken link.
        Assert.False(elements[2].TryGetProperty("image_url", out var none) && none.ValueKind != JsonValueKind.Null);

        // Meta fetches the links without signing in; each opens its own photo.
        using var meta = Api.CreateClient();
        Assert.Equal(
            await owner.Http.GetByteArrayAsync($"/api/v1/products/{bag.Id}/images/{main}"),
            await meta.GetByteArrayAsync(new Uri(black).AbsolutePath));
        Assert.Equal(
            await owner.Http.GetByteArrayAsync($"/api/v1/products/{bag.Id}/images/{red}"),
            await meta.GetByteArrayAsync(new Uri(redLink).AbsolutePath));
        Assert.DoesNotContain(main, black, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> PhotoAsync(ApiClient owner, string productId, SkiaSharp.SKColor colour)
    {
        var response = await Catalog.ProductImagesTests.UploadAsync(owner, productId, Inbox.MediaImageTests.Png(30, 20, colour));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task A_tap_on_our_button_is_a_customer_message_and_anything_else_is_plain_text()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var (id, psid) = await ConversationAsync(owner, pageId);
        var bag = await CreateProductAsync(owner, "Borsa Milano", ("Nero", 8900));
        var buy = (await SendCarouselAsync(owner, id, bag))[0];

        await PostAsync(PostbackWebhook(pageId, psid, "pb-1-" + psid, "🛒 Lo voglio", buy));
        var tap = await LastMessageAsync(owner, id);
        Assert.Equal("tap", tap.GetProperty("kind").GetString());
        Assert.Equal("customer", tap.GetProperty("author").GetString());
        Assert.Equal("🛒 Lo voglio — Borsa Milano", tap.GetProperty("text").GetString());

        // The same payload as a quick reply also counts, once per webhook delivery.
        await PostAsync(QuickReplyWebhook(pageId, psid, "qr-1-" + psid, "🛒 Lo voglio", buy));
        await PostAsync(QuickReplyWebhook(pageId, psid, "qr-1-" + psid, "🛒 Lo voglio", buy));
        var history = await HistoryAsync(owner, id);
        Assert.Equal(2, history.Count(m => m.GetProperty("kind").GetString() == "tap"));

        // Forged and malformed payloads are only words.
        await PostAsync(PostbackWebhook(pageId, psid, "pb-2-" + psid, "🛒 Lo voglio", "rm1:AAAAAAAAAAAAAAAAAAAAAA"));
        Assert.Equal("text", (await LastMessageAsync(owner, id)).GetProperty("kind").GetString());
        await PostAsync(PostbackWebhook(pageId, psid, "pb-3-" + psid, "Lo voglio", "{\"productId\":\"" + bag.Id + "\"}"));
        var forged = await LastMessageAsync(owner, id);
        Assert.Equal("text", forged.GetProperty("kind").GetString());
        Assert.Equal("Lo voglio", forged.GetProperty("text").GetString());
    }

    [Fact]
    public async Task A_token_from_another_conversation_or_workspace_or_an_old_one_never_acts()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var (id, _) = await ConversationAsync(owner, pageId);
        var (otherId, otherPsid) = await ConversationAsync(owner, pageId);
        var bag = await CreateProductAsync(owner, "Borsa Milano", ("Nero", 8900));
        var buy = (await SendCarouselAsync(owner, id, bag))[0];

        // Another customer of the same Page sends the first customer's payload.
        await PostAsync(PostbackWebhook(pageId, otherPsid, "pb-x-" + otherPsid, "🛒 Lo voglio", buy));
        Assert.Equal("text", (await LastMessageAsync(owner, otherId)).GetProperty("kind").GetString());

        // Another workspace's Page receives it.
        var (ownerB, pageB) = await ConnectedOwnerAsync();
        var (idB, psidB) = await ConversationAsync(ownerB, pageB);
        await PostAsync(PostbackWebhook(pageB, psidB, "pb-b-" + psidB, "🛒 Lo voglio", buy));
        Assert.Equal("text", (await LastMessageAsync(ownerB, idB)).GetProperty("kind").GetString());

        // After 7 days the button only reads as text.
        var (oldId, oldPsid) = await ConversationAsync(owner, pageId);
        var old = (await SendCarouselAsync(owner, oldId, bag))[0];
        Api.Clock.Advance(TimeSpan.FromDays(8));
        try
        {
            await PostAsync(PostbackWebhook(pageId, oldPsid, "pb-old-" + oldPsid, "🛒 Lo voglio", old));
        }
        finally
        {
            Api.Clock.Advance(TimeSpan.FromDays(-8));
        }

        Assert.Equal("text", (await LastMessageAsync(owner, oldId)).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_tap_for_a_product_that_is_no_longer_active_is_plain_text_and_dropped_cards_are_not_sent()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var (id, psid) = await ConversationAsync(owner, pageId);
        var bag = await CreateProductAsync(owner, "Borsa Milano", ("Nero", 8900));
        var buy = (await SendCarouselAsync(owner, id, bag))[0];

        await DeactivateAsync(owner, bag);
        await PostAsync(PostbackWebhook(pageId, psid, "pb-in-" + psid, "🛒 Lo voglio", buy));
        Assert.Equal("text", (await LastMessageAsync(owner, id)).GetProperty("kind").GetString());

        // A carousel queued before the product went inactive is sent without it: here, as text.
        var scarf = await CreateProductAsync(owner, "Sciarpa", ("Blu", 2500));
        await owner.PostAsync($"/api/v1/conversations/{id}/products", new { items = new[] { new { productId = scarf.Id } } });
        await DeactivateAsync(owner, scarf);
        Assert.Equal(1, await Api.SendRepliesAsync());
        using var body = JsonDocument.Parse(Api.Meta.Sends[^1].Body);
        Assert.Equal("Questi prodotti non sono più disponibili.", body.RootElement.GetProperty("message").GetProperty("text").GetString());
        var shown = await LastMessageAsync(owner, id);
        Assert.True(shown.GetProperty("fallbackText").GetBoolean());
        Assert.Empty(shown.GetProperty("rich").GetProperty("cards").EnumerateArray());
    }

    [Fact]
    public async Task When_Meta_refuses_the_carousel_a_numbered_text_list_goes_and_the_team_is_told()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var (id, _) = await ConversationAsync(owner, pageId);
        var bag = await CreateProductAsync(owner, "Borsa Milano", ("Nero", 8900));
        await owner.PostAsync($"/api/v1/conversations/{id}/products", new { items = new[] { new { productId = bag.Id, variantId = bag.Variants[0] } } });

        Api.Meta.RejectRichSends = 1;
        try
        {
            Assert.Equal(1, await Api.SendRepliesAsync());
        }
        finally
        {
            Api.Meta.RejectRichSends = 0;
        }

        using var body = JsonDocument.Parse(Api.Meta.Sends[^1].Body);
        Assert.Equal(
            "1. Borsa Milano — €89.00 · Nero\nRispondi con il numero del prodotto che ti interessa.",
            body.RootElement.GetProperty("message").GetProperty("text").GetString());
        var shown = await LastMessageAsync(owner, id);
        Assert.Equal("sent", shown.GetProperty("delivery").GetString());
        Assert.True(shown.GetProperty("fallbackText").GetBoolean());
    }

    [Fact]
    public async Task Taps_open_the_24_hour_window_again()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var (id, psid) = await ConversationAsync(owner, pageId);
        var bag = await CreateProductAsync(owner, "Borsa Milano", ("Nero", 8900));
        var buy = (await SendCarouselAsync(owner, id, bag))[0];

        Api.Clock.Advance(TimeSpan.FromHours(30));
        try
        {
            // The old session has timed out by then; sign in again.
            using var again = Api.CreateApiClient();
            (await again.LoginAsync(OwnerEmail)).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Conflict, (await again.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Late" })).StatusCode);
            await PostAsync(PostbackWebhook(pageId, psid, "pb-w-" + psid, "🛒 Lo voglio", buy));
            Assert.Equal(HttpStatusCode.Created, (await again.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Eccoci" })).StatusCode);
        }
        finally
        {
            Api.Clock.Advance(TimeSpan.FromHours(-30));
        }
    }

    /// <summary>Sends a carousel with one card and returns the card's button payloads, as Meta received them.</summary>
    private async Task<List<string>> SendCarouselAsync(ApiClient owner, string id, CreatedProduct product)
    {
        (await owner.PostAsync($"/api/v1/conversations/{id}/products", new { items = new[] { new { productId = product.Id, variantId = product.Variants[0] } } })).EnsureSuccessStatusCode();
        Assert.Equal(1, await Api.SendRepliesAsync());
        using var body = JsonDocument.Parse(Api.Meta.Sends[^1].Body);
        return body.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("elements")[0]
            .GetProperty("buttons").EnumerateArray().Select(b => b.GetProperty("payload").GetString()!).ToList();
    }

    private async Task<(ApiClient Owner, string PageId)> ConnectedOwnerAsync()
    {
        var pageId = NewId();
        Api.Meta.Pages.Clear();
        Api.Meta.Pages.Add((pageId, "Page " + pageId, "page-token-" + pageId));
        var owner = Api.CreateApiClient(followRedirects: false);
        OwnerEmail = AuthFixture.NewEmail();
        await owner.SignupAndVerifyAsync(Api.Emails, OwnerEmail, "Almanaam");
        var start = await (await owner.PostAsync("/api/v1/channels/meta/connect")).Content.ReadFromJsonAsync<JsonElement>();
        var state = HttpUtility.ParseQueryString(new Uri(start.GetProperty("url").GetString()!).Query)["state"]!;
        var callback = await owner.Http.GetAsync($"/api/v1/channels/meta/callback?code=fake-code&state={Uri.EscapeDataString(state)}");
        Assert.EndsWith("connected=1&elsewhere=0", callback.Headers.Location!.ToString(), StringComparison.Ordinal);
        return (owner, pageId);
    }

    private async Task<(string Id, string Psid)> ConversationAsync(ApiClient owner, string pageId)
    {
        var psid = NewId();
        await PostAsync(Envelope(pageId, new { sender = new { id = psid }, recipient = new { id = pageId }, timestamp = Now(), message = new { mid = "m-" + psid, text = "Ciao" } }));
        // Newest activity first: the conversation just started.
        var list = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/conversations");
        return (list.GetProperty("items")[0].GetProperty("id").GetString()!, psid);
    }

    private string PostbackWebhook(string pageId, string psid, string mid, string title, string payload) =>
        Envelope(pageId, new { sender = new { id = psid }, recipient = new { id = pageId }, timestamp = Now(), postback = new { mid, title, payload } });

    private string QuickReplyWebhook(string pageId, string psid, string mid, string text, string payload) =>
        Envelope(pageId, new { sender = new { id = psid }, recipient = new { id = pageId }, timestamp = Now(), message = new { mid, text, quick_reply = new { payload } } });

    private string Envelope(string pageId, object messaging) =>
        JsonSerializer.Serialize(new { @object = "page", entry = new[] { new { id = pageId, time = Now(), messaging = new[] { messaging } } } });

    private long Now() => Api.Clock.GetUtcNow().ToUnixTimeMilliseconds();

    private async Task PostAsync(string body)
    {
        using var meta = Api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, Webhook) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Hub-Signature-256",
            "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(FakeMetaGraphHandler.AppSecret), Encoding.UTF8.GetBytes(body))));
        Assert.Equal(HttpStatusCode.OK, (await meta.SendAsync(request)).StatusCode);
        await Api.ProcessMetaWebhooksAsync();
    }

    private static async Task<List<JsonElement>> HistoryAsync(ApiClient owner, string id) =>
        (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items").EnumerateArray().ToList();

    private static async Task<JsonElement> LastMessageAsync(ApiClient owner, string id) => (await HistoryAsync(owner, id))[^1];

    private sealed record CreatedProduct(string Id, List<string> Variants);

    private static async Task<CreatedProduct> CreateProductAsync(ApiClient owner, string title, params (string Option, long Price)[] variants)
    {
        var created = await owner.PostAsync("/api/v1/products", ProductBody(title, "active", variants));
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return new CreatedProduct(
            body.GetProperty("id").GetString()!,
            body.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("id").GetString()!).ToList());
    }

    private static async Task DeactivateAsync(ApiClient owner, CreatedProduct product)
    {
        var current = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{product.Id}");
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.GetRawText())!;
        body["status"] = JsonSerializer.SerializeToElement("draft");
        await owner.RefreshCsrfAsync();
        (await owner.Http.PutAsJsonAsync($"/api/v1/products/{product.Id}", body)).EnsureSuccessStatusCode();
    }

    private static object ProductBody(string title, string status, (string Option, long Price)[] variants) => new
    {
        title,
        status,
        optionNames = Options,
        variants = variants.Select(v => new { sku = $"S-{Guid.NewGuid():N}"[..12], option1 = v.Option, price = v.Price }).ToArray(),
    };

    private static string NewId() => Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
