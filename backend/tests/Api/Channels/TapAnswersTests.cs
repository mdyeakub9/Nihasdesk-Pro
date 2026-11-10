using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Channels;

/// <summary>
/// Taps answered by code and the AI's rich tools (Issue #91 PR 2a, ADR 0032 §4–§5, §7): options one at
/// a time from the catalog, no model call for taps, show_products only with this turn's ids, no product
/// lists in text, and team carousels (rule 5, SEC-36, SEC-38).
/// </summary>
[Collection(AuthTests.Name)]
public sealed class TapAnswersTests(AuthFixture fixture)
{
    private const string Webhook = "/api/v1/webhooks/meta";
    private static readonly string[] TwoOptions = ["Colore", "Taglia"];

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task The_ai_shows_a_carousel_and_taps_lead_to_in_stock_options_without_the_model()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok") : call.ToolResults.Count switch
        {
            0 => FakeAiAnswer.Call("search_products", """{"query":"borsa"}"""),
            1 => FakeAiAnswer.Call("show_products", JsonSerializer.Serialize(new { product_ids = new[] { shop.BagId } })),
            _ => FakeAiAnswer.Say("Ecco cosa abbiamo 👇"),
        };

        var psid = await CustomerWritesAsync(shop, $"Avete borse? {marker}");
        await Api.RunAiAsync();
        await Api.SendRepliesAsync();
        var carousel = LastSendTo(psid);
        var cards = carousel.GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("elements");
        Assert.Equal("Borsa Milano", cards[0].GetProperty("title").GetString());
        var buy = cards[0].GetProperty("buttons")[0].GetProperty("payload").GetString()!;
        var aiCalls = Api.Ai.Calls.Count;

        // Lo voglio → colours in stock (Blu is sold out) as quick replies.
        await TapAsync(shop, psid, "🛒 Lo voglio", postback: buy);
        var colours = QuickReplies(LastSendTo(psid));
        Assert.Equal(["Nero", "Rosso"], colours.Select(q => q.Title));
        Assert.Contains("colore", LastSendTo(psid).GetProperty("message").GetProperty("text").GetString(), StringComparison.Ordinal);

        // Nero → sizes of Nero in stock.
        await TapAsync(shop, psid, "Nero", quickReply: colours[0].Payload);
        var sizes = QuickReplies(LastSendTo(psid));
        Assert.Equal(["M", "L"], sizes.Select(q => q.Title));

        // L → the item, with the catalog price, and the details still needed.
        await TapAsync(shop, psid, "L", quickReply: sizes[1].Payload);
        var done = LastSendTo(psid).GetProperty("message").GetProperty("text").GetString()!;
        Assert.StartsWith("Perfetto: Borsa Milano — Nero / L, €89.00.", done, StringComparison.Ordinal);

        // No model call for any tap; every tap is still traced (ADR 0032 §4).
        Assert.Equal(aiCalls, Api.Ai.Calls.Count);
        var runs = await RunsAsync(shop);
        Assert.Equal(3, runs.Count(r => r.Source == RunSource.Tap && r.Outcome == RunOutcome.Replied && r.InputTokens == 0));

        // The next written message: the model gets the chosen item from code.
        var next = Marker();
        Api.Ai.Respond = call => FakeAiAnswer.Say("Grazie!");
        await CustomerWritesAsync(shop, $"Giulia Rossi, via Roma 12 Sassari {next}", psid);
        await Api.RunAiAsync();
        var call = Api.Ai.Calls.Last(c => Mine(c, next));
        Assert.Contains("Items the customer already chose", call.System, StringComparison.Ordinal);
        Assert.Contains(shop.NeroL, call.System, StringComparison.Ordinal);

        // Leaves no queued reply behind for the other tests of this collection.
        await Api.SendRepliesAsync();
    }

    [Fact]
    public async Task A_card_for_one_variant_is_only_confirmed_and_similar_shows_other_products()
    {
        var shop = await ShopAsync();
        var (id, psid) = await TeamConversationAsync(shop);
        await ReturnToAiAsync(shop, id);
        await shop.Owner.PostAsync($"/api/v1/conversations/{id}/products", new { items = new[] { new { productId = shop.BagId, variantId = shop.NeroL } } });
        await ReturnToAiAsync(shop, id);
        await Api.SendRepliesAsync();
        var buttons = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("elements")[0].GetProperty("buttons");

        await TapAsync(shop, psid, "🛒 Lo voglio", postback: buttons[0].GetProperty("payload").GetString()!);
        var confirm = QuickReplies(LastSendTo(psid));
        Assert.Equal(["✅ Sì, questa", "📏 Altre opzioni"], confirm.Select(q => q.Title));
        Assert.StartsWith("Borsa Milano — Nero / L, €89.00.", LastSendTo(psid).GetProperty("message").GetProperty("text").GetString(), StringComparison.Ordinal);

        await TapAsync(shop, psid, "🔍 Simili", postback: buttons[2].GetProperty("payload").GetString()!);
        var similar = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("elements");
        var titles = similar.EnumerateArray().Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Contains("Borsa Roma", titles);
        Assert.DoesNotContain("Borsa Milano", titles);
        Assert.Contains("Simile", similar[0].GetProperty("subtitle").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_a_team_conversation_code_answers_options_and_leaves_everything_else_to_the_team()
    {
        var shop = await ShopAsync();
        var (id, psid) = await TeamConversationAsync(shop);
        await shop.Owner.PostAsync($"/api/v1/conversations/{id}/products", new { items = new[] { new { productId = shop.BagId } } });
        await Api.SendRepliesAsync();
        var buttons = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("elements")[0].GetProperty("buttons");
        var sends = Api.Meta.Sends.Count;

        // "Lo voglio" on the team's carousel: the team answers, the chat waits for them.
        await TapAsync(shop, psid, "🛒 Lo voglio", postback: buttons[0].GetProperty("payload").GetString()!);
        Assert.Equal(sends, Api.Meta.Sends.Count);
        var conversation = (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("human", conversation.GetProperty("responder").GetString());
        Assert.Equal("waiting", conversation.GetProperty("status").GetString());

        // "Taglie e colori": code still offers the options; the team keeps the conversation.
        await TapAsync(shop, psid, "📏 Taglie e colori", postback: buttons[1].GetProperty("payload").GetString()!);
        Assert.Equal(["Nero", "Rosso"], QuickReplies(LastSendTo(psid)).Select(q => q.Title));
        conversation = (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("human", conversation.GetProperty("responder").GetString());
    }

    [Fact]
    public async Task Talk_to_a_person_hands_over_and_show_products_takes_only_ids_from_this_turn()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        var invented = Guid.NewGuid();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok") : call.ToolResults.Count switch
        {
            // A remembered or made-up id: refused, no carousel.
            0 => FakeAiAnswer.Call("show_products", JsonSerializer.Serialize(new { product_ids = new[] { shop.BagId, invented.ToString() } })),
            _ => FakeAiAnswer.Say("Un attimo che controllo."),
        };

        var psid = await CustomerWritesAsync(shop, $"Cosa avete? {marker}");
        await Api.RunAiAsync();
        var result = Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults.Single();
        Assert.Contains("search_products or get_product_options returned in this turn", result, StringComparison.Ordinal);
        await Api.SendRepliesAsync();
        Assert.False(LastSendTo(psid).GetProperty("message").TryGetProperty("attachment", out _));

        // Choices only from tool results: "Spedizione gratis" was never said by a tool.
        var choices = Marker();
        Api.Ai.Respond = call => !Mine(call, choices) ? FakeAiAnswer.Say("ok") : call.ToolResults.Count switch
        {
            0 => FakeAiAnswer.Call("get_shipping_options", "{}"),
            1 => FakeAiAnswer.Call("show_choices", """{"options":["Sardegna","Spedizione gratis"]}"""),
            2 => FakeAiAnswer.Call("show_choices", """{"options":["Sardegna"]}"""),
            _ => FakeAiAnswer.Say("Dove la spediamo?"),
        };
        await CustomerWritesAsync(shop, $"Dove spedite? {choices}", psid);
        await Api.RunAiAsync();
        var shown = Api.Ai.Calls.Last(c => Mine(c, choices)).ToolResults;
        Assert.Contains("a value a tool returned in this turn", shown[1], StringComparison.Ordinal);
        await Api.SendRepliesAsync();
        var zones = LastSendTo(psid);
        Assert.Equal("Dove la spediamo?", zones.GetProperty("message").GetProperty("text").GetString());
        Assert.Equal(["Sardegna"], QuickReplies(zones).Select(q => q.Title));

        // Nothing similar to an umbrella → "Parla con noi" → the team takes over with a note.
        var umbrella = (await ProductAsync(shop.Owner, "Ombrello", ("Blu", "Unica", 1500, 5), category: "Accessori")).GetProperty("id").GetString();
        var (id, _) = await IdOfAsync(shop, psid);
        await shop.Owner.PostAsync($"/api/v1/conversations/{id}/products", new { items = new[] { new { productId = umbrella } } });
        await ReturnToAiAsync(shop, id);
        await Api.SendRepliesAsync();
        var similar = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("elements")[0].GetProperty("buttons")[2];
        await TapAsync(shop, psid, "🔍 Simili", postback: similar.GetProperty("payload").GetString()!);
        var person = Assert.Single(QuickReplies(LastSendTo(psid)));
        Assert.Equal("Parla con noi", person.Title);

        await TapAsync(shop, psid, "Parla con noi", quickReply: person.Payload);
        var conversation = (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("human", conversation.GetProperty("responder").GetString());
        var notes = (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items").EnumerateArray();
        Assert.Contains(notes, m => m.GetProperty("internal").GetBoolean() && m.GetProperty("text").GetString() == "The customer asked for a person.");
    }

    [Fact]
    public async Task A_reply_that_lists_more_than_two_products_in_text_is_held_back()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok") : call.ToolResults.Count switch
        {
            0 => FakeAiAnswer.Call("search_products", """{"query":"borsa"}"""),
            _ => FakeAiAnswer.Say("Abbiamo Borsa Milano, Borsa Roma e Borsa Napoli."),
        };

        var psid = await CustomerWritesAsync(shop, $"Che borse avete? {marker}");
        await Api.RunAiAsync();

        var (id, _) = await IdOfAsync(shop, psid);
        var conversation = (await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("human", conversation.GetProperty("responder").GetString());
        Assert.Contains(await RunsAsync(shop), r => r.Detail == "products_in_text");
    }

    [Fact]
    public async Task A_carousel_intro_with_a_price_no_tool_gave_is_held_back_like_the_reply()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok") : call.ToolResults.Count switch
        {
            0 => FakeAiAnswer.Call("search_products", """{"query":"borsa"}"""),
            1 => FakeAiAnswer.Call("show_products", JsonSerializer.Serialize(new { product_ids = new[] { shop.BagId }, intro = "Tutte sotto €50!" })),
            _ => FakeAiAnswer.Say("Ecco 👇"),
        };

        var psid = await CustomerWritesAsync(shop, $"Borse economiche? {marker}");
        await Api.RunAiAsync();

        Assert.Contains(await RunsAsync(shop), r => r.Detail == "price_not_from_tool");
        await Api.SendRepliesAsync();
        Assert.DoesNotContain(Api.Meta.Sends, s => s.Body.Contains(psid, StringComparison.Ordinal) && s.Body.Contains("€50", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_call_that_runs_out_of_tokens_is_tried_once_more_before_handing_over()
    {
        var shop = await ShopAsync();
        var once = Marker();
        Api.Ai.Respond = call => FakeAiAnswer.Say("Certo, ti aiuto subito.");

        Api.Ai.OutputLimits = 1;
        var psid = await CustomerWritesAsync(shop, $"Ciao {once}");
        await Api.RunAiAsync();
        await Api.SendRepliesAsync();
        Assert.Equal("Certo, ti aiuto subito.", LastSendTo(psid).GetProperty("message").GetProperty("text").GetString());

        // Twice in a row: the team takes over, as before.
        Api.Ai.OutputLimits = 2;
        await CustomerWritesAsync(shop, $"E poi? {Marker()}", psid);
        await Api.RunAiAsync();
        Api.Ai.OutputLimits = 0;
        Assert.Contains(await RunsAsync(shop), r => r.Outcome == RunOutcome.Failed && r.Detail == "output_limit");
    }

    // --- summary with Conferma / Modifica (PR 2b) -------------------------------------------------

    [Fact]
    public async Task The_summary_comes_with_Conferma_and_a_tap_creates_a_pending_request_from_the_catalog_prices()
    {
        var shop = await ShopAsync();
        var psid = await QuoteWithDetailsAsync(shop, quantity: 2);
        var summary = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload");
        Assert.Equal("button", summary.GetProperty("template_type").GetString());
        var text = summary.GetProperty("text").GetString()!;
        Assert.Contains("2 × Borsa Milano — Nero / L: €178.00", text, StringComparison.Ordinal);
        Assert.Contains("Spedizione (Sardegna): €7.00", text, StringComparison.Ordinal);
        Assert.Contains("Totale da pagare alla consegna: €185.00", text, StringComparison.Ordinal);
        var buttons = summary.GetProperty("buttons");
        Assert.Equal(["✅ Conferma", "✏️ Modifica"], buttons.EnumerateArray().Select(b => b.GetProperty("title").GetString()));
        var aiCalls = Api.Ai.Calls.Count;

        await TapAsync(shop, psid, "✅ Conferma", postback: buttons[0].GetProperty("payload").GetString()!);

        var thanks = LastSendTo(psid).GetProperty("message").GetProperty("text").GetString()!;
        Assert.StartsWith("Grazie! Abbiamo ricevuto la tua richiesta d'ordine n. ", thanks, StringComparison.Ordinal);
        Assert.Contains("totale €185.00 da pagare alla consegna", thanks, StringComparison.Ordinal);
        Assert.DoesNotContain("confermato", thanks, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(aiCalls, Api.Ai.Calls.Count);
        var order = Assert.Single((await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=pending")).GetProperty("items").EnumerateArray());
        Assert.Equal("pending", order.GetProperty("status").GetString());
        Assert.Equal(18500, order.GetProperty("total").GetInt64());
        Assert.Empty(await DraftsAsync(shop));

        // The same Conferma again: nothing more is created.
        await TapAsync(shop, psid, "✅ Conferma", postback: buttons[0].GetProperty("payload").GetString()!);
        Assert.Single((await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=pending")).GetProperty("items").EnumerateArray());
        await Api.SendRepliesAsync();
    }

    [Fact]
    public async Task A_changed_price_or_an_old_summary_answers_with_a_fresh_summary_and_creates_nothing()
    {
        var shop = await ShopAsync();
        var psid = await QuoteWithDetailsAsync(shop, quantity: 1);
        var first = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("buttons")[0].GetProperty("payload").GetString()!;

        await SetPriceAsync(shop, shop.NeroL, 9900);
        await TapAsync(shop, psid, "✅ Conferma", postback: first);
        var fresh = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload");
        Assert.StartsWith("Il riepilogo è cambiato, controlla di nuovo:", fresh.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains("€99.00", fresh.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Empty((await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=pending")).GetProperty("items").EnumerateArray());

        // The first summary's button is old now: again only the current summary.
        await TapAsync(shop, psid, "✅ Conferma", postback: first);
        Assert.Empty((await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=pending")).GetProperty("items").EnumerateArray());

        // The current one works.
        var current = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("buttons")[0].GetProperty("payload").GetString()!;
        await TapAsync(shop, psid, "✅ Conferma", postback: current);
        Assert.Equal(10600, Assert.Single((await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=pending")).GetProperty("items").EnumerateArray()).GetProperty("total").GetInt64());
        await Api.SendRepliesAsync();
    }

    [Fact]
    public async Task Modifica_asks_what_to_change_and_the_old_Conferma_no_longer_creates_anything()
    {
        var shop = await ShopAsync();
        var psid = await QuoteWithDetailsAsync(shop, quantity: 1);
        var buttons = LastSendTo(psid).GetProperty("message").GetProperty("attachment").GetProperty("payload").GetProperty("buttons");

        await TapAsync(shop, psid, "✏️ Modifica", postback: buttons[1].GetProperty("payload").GetString()!);
        Assert.Equal("Certo! Cosa vuoi cambiare?", LastSendTo(psid).GetProperty("message").GetProperty("text").GetString());

        // Conferma after Modifica reaches the model as words; code creates nothing.
        Api.Ai.Respond = _ => FakeAiAnswer.Say("Dimmi pure cosa cambiare.");
        await TapAsync(shop, psid, "✅ Conferma", postback: buttons[0].GetProperty("payload").GetString()!);
        Assert.Empty((await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/orders?status=pending")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Details_the_order_rules_refuse_get_no_buttons_and_the_model_asks_again()
    {
        var shop = await ShopAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok") : call.ToolResults.Count switch
        {
            0 => FakeAiAnswer.Call("quote_order", QuoteArgs(shop, 1, phone: "chiamami")),
            _ => FakeAiAnswer.Say("Mi lasci un numero di telefono valido?"),
        };

        var psid = await CustomerWritesAsync(shop, $"Ordino! {marker}");
        await Api.RunAiAsync();
        var result = Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults.Single();
        Assert.Contains("\"summaryWithButtons\":false", result, StringComparison.Ordinal);
        Assert.Contains("Ask the customer again for: Phone", result, StringComparison.Ordinal);
        await Api.SendRepliesAsync();
        Assert.False(LastSendTo(psid).GetProperty("message").TryGetProperty("attachment", out _));
    }

    /// <summary>The AI quotes Nero / L with all delivery details; returns the customer's PSID, the summary sent.</summary>
    private async Task<string> QuoteWithDetailsAsync(Shop shop, int quantity)
    {
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok") : call.ToolResults.Count switch
        {
            0 => FakeAiAnswer.Call("quote_order", QuoteArgs(shop, quantity)),
            _ => FakeAiAnswer.Say("Ecco il riepilogo: controlla e tocca Conferma 👇"),
        };

        var psid = await CustomerWritesAsync(shop, $"Giulia Rossi, via Roma 12, 07100 Sassari SS, +39 333 1234567 {marker}");
        await Api.RunAiAsync();
        await Api.SendRepliesAsync();
        return psid;
    }

    private static string QuoteArgs(Shop shop, int quantity, string phone = "+39 333 1234567") =>
        JsonSerializer.Serialize(new
        {
            items = new[] { new { product_id = shop.BagId, variant_id = shop.NeroL, quantity } },
            zone_id = shop.ZoneId,
            customer_name = "Giulia Rossi",
            phone,
            street = "Via Roma 12",
            postal_code = "07100",
            city = "Sassari",
            province = "SS",
            country = "IT",
        });

    private static async Task SetPriceAsync(Shop shop, string variantId, long price)
    {
        var current = await shop.Owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{shop.BagId}");
        var body = System.Text.Json.Nodes.JsonNode.Parse(current.GetRawText())!.AsObject();
        foreach (var variant in body["variants"]!.AsArray())
        {
            if (variant!["id"]!.GetValue<string>() == variantId)
            {
                variant["price"] = price;
            }
        }

        await shop.Owner.RefreshCsrfAsync();
        (await shop.Owner.Http.PutAsJsonAsync($"/api/v1/products/{shop.BagId}", body)).EnsureSuccessStatusCode();
    }

    private async Task<List<OrderDraft>> DraftsAsync(Shop shop)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(shop.Workspace));
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>().Drafts.AsNoTracking().ToListAsync();
    }

    // --- shop and channel -------------------------------------------------------------------------

    private sealed record Shop(ApiClient Owner, string Workspace, string PageId, string BagId, string NeroL, string ZoneId = "");

    private async Task<Shop> ShopAsync()
    {
        var pageId = NewId();
        Api.Meta.Pages.Clear();
        Api.Meta.Pages.Add((pageId, "Page " + pageId, "page-token-" + pageId));
        var owner = Api.CreateApiClient(followRedirects: false);
        var me = await (await owner.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Bottega Taps")).Content.ReadFromJsonAsync<JsonElement>();
        var start = await (await owner.PostAsync("/api/v1/channels/meta/connect")).Content.ReadFromJsonAsync<JsonElement>();
        var state = HttpUtility.ParseQueryString(new Uri(start.GetProperty("url").GetString()!).Query)["state"]!;
        await owner.Http.GetAsync($"/api/v1/channels/meta/callback?code=fake-code&state={Uri.EscapeDataString(state)}");

        var bag = await ProductAsync(owner, "Borsa Milano",
            ("Nero", "M", 8900, 3), ("Nero", "L", 8900, 2), ("Rosso", "M", 9500, 1), ("Blu", "S", 8900, 0));
        await ProductAsync(owner, "Borsa Roma", ("Nero", "M", 7900, 4));
        await ProductAsync(owner, "Borsa Napoli", ("Nero", "M", 6900, 4));
        await owner.RefreshCsrfAsync();
        var zones = await (await owner.Http.PutAsJsonAsync("/api/v1/shipping/zones", new
        {
            zones = new[] { new { name = "Sardegna", areas = "SS", price = 700, currency = "EUR", freeOver = (long?)null, minDays = 2, maxDays = 4 } },
        })).Content.ReadFromJsonAsync<JsonElement>();
        var variants = bag.GetProperty("variants").EnumerateArray().ToList();
        return new Shop(owner, me.GetProperty("workspace").GetProperty("id").GetString()!, pageId,
            bag.GetProperty("id").GetString()!, variants[1].GetProperty("id").GetString()!, zones[0].GetProperty("id").GetString()!);
    }

    private static Task<JsonElement> ProductAsync(ApiClient owner, string title, params (string Colour, string Size, long Price, int Stock)[] variants) =>
        ProductAsync(owner, title, variants[0], "Borse", variants);

    private static Task<JsonElement> ProductAsync(ApiClient owner, string title, (string Colour, string Size, long Price, int Stock) only, string category) =>
        ProductAsync(owner, title, only, category, [only]);

    private static async Task<JsonElement> ProductAsync(ApiClient owner, string title, (string, string, long, int) _, string category, (string Colour, string Size, long Price, int Stock)[] variants)
    {
        var created = await owner.PostAsync("/api/v1/products", new
        {
            title,
            status = "active",
            category,
            optionNames = TwoOptions,
            variants = variants.Select(v => new
            {
                sku = $"T-{Guid.NewGuid():N}"[..12],
                option1 = v.Colour,
                option2 = v.Size,
                price = v.Price,
                trackInventory = true,
                stock = v.Stock,
                inventoryPolicy = "deny",
            }).ToArray(),
        });
        created.EnsureSuccessStatusCode();
        return await created.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<string> CustomerWritesAsync(Shop shop, string text, string? psid = null)
    {
        psid ??= NewId();
        await PostAsync(Envelope(shop.PageId, new { sender = new { id = psid }, recipient = new { id = shop.PageId }, timestamp = Now(), message = new { mid = "m-" + Guid.NewGuid().ToString("N"), text } }));
        return psid;
    }

    /// <summary>A conversation the team has taken over (a team reply).</summary>
    private async Task<(string Id, string Psid)> TeamConversationAsync(Shop shop)
    {
        var psid = await CustomerWritesAsync(shop, "Ciao");
        var (id, _) = await IdOfAsync(shop, psid);
        await shop.Owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Eccoci!" });
        await Api.SendRepliesAsync();
        return (id, psid);
    }

    private static async Task ReturnToAiAsync(Shop shop, string id)
    {
        await shop.Owner.RefreshCsrfAsync();
        (await shop.Owner.Http.PatchAsJsonAsync($"/api/v1/conversations/{id}", new { responder = "ai" })).EnsureSuccessStatusCode();
    }

    private static async Task<(string Id, string Psid)> IdOfAsync(Shop shop, string psid)
    {
        var list = await shop.Owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/conversations");
        return (list.GetProperty("items")[0].GetProperty("id").GetString()!, psid);
    }

    private async Task TapAsync(Shop shop, string psid, string label, string? postback = null, string? quickReply = null)
    {
        var mid = "tap-" + Guid.NewGuid().ToString("N");
        object messaging = postback is not null
            ? new { sender = new { id = psid }, recipient = new { id = shop.PageId }, timestamp = Now(), postback = new { mid, title = label, payload = postback } }
            : new { sender = new { id = psid }, recipient = new { id = shop.PageId }, timestamp = Now(), message = new { mid, text = label, quick_reply = new { payload = quickReply } } };
        await PostAsync(Envelope(shop.PageId, messaging));
        await Api.RunAiAsync();
        await Api.SendRepliesAsync();
    }



    private JsonElement LastSendTo(string psid)
    {
        var send = Api.Meta.Sends.Last(s => s.Body.Contains(psid, StringComparison.Ordinal));
        return JsonDocument.Parse(send.Body).RootElement.Clone();
    }

    private static List<(string Title, string Payload)> QuickReplies(JsonElement send) =>
        [.. send.GetProperty("message").GetProperty("quick_replies").EnumerateArray()
            .Select(q => (q.GetProperty("title").GetString()!, q.GetProperty("payload").GetString()!))];

    private async Task<List<AiRun>> RunsAsync(Shop shop)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(shop.Workspace));
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>().Runs.AsNoTracking().ToListAsync();
    }


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

    private static string Marker() => "#" + Guid.NewGuid().ToString("N")[..8];

    private static bool Mine(FakeAiCall call, string marker) =>
        call.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);

    private static string NewId() => Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
