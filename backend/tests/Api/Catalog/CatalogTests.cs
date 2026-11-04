using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.TestSupport;
using Npgsql;

namespace Nihasdesk.Api.Tests.Catalog;

/// <summary>Products, variants and the Shopify CSV import (ADR 0018, SEC-04/06/10/11/21).</summary>
[Collection(AuthTests.Name)]
public sealed class CatalogTests(AuthFixture fixture)
{
    private static readonly string[] ColourOption = ["Colore"];

    private NihasdeskApiFactory Api => fixture.Api;

    private static object Bag(string title = "Classic Leather Bag", string skuPrefix = "BAG") => new
    {
        title,
        description = "Soft Italian leather.",
        status = "active",
        productType = "Bag",
        category = "Handbags",
        vendor = "Almanaam",
        tags = new[] { "leather", "gift", "leather" },
        optionNames = new[] { "Colour" },
        variants = new object[]
        {
            new { sku = $"{skuPrefix}-BLK", option1 = "Black", price = 8900, compareAtPrice = 11900, cost = 4500, currency = "eur", trackInventory = true, stock = 12, inventoryPolicy = "deny", weightGrams = 800 },
            new { sku = $"{skuPrefix}-BRN", option1 = "Brown", price = 8900, trackInventory = true, stock = 0 },
        },
    };

    [Fact]
    public async Task Owner_creates_a_product_with_variants_and_a_free_handle()
    {
        var (owner, _) = await OwnerAsync();

        var created = await owner.PostAsync("/api/v1/products", Bag());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("classic-leather-bag", product.GetProperty("handle").GetString());
        Assert.Equal(["leather", "gift"], product.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        var variants = product.GetProperty("variants");
        Assert.Equal(2, variants.GetArrayLength());
        Assert.Equal("EUR", variants[0].GetProperty("currency").GetString());
        Assert.True(variants[0].GetProperty("available").GetBoolean());
        Assert.False(variants[1].GetProperty("available").GetBoolean());

        var again = await (await owner.PostAsync("/api/v1/products", Bag(skuPrefix: "BAG2"))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("classic-leather-bag-2", again.GetProperty("handle").GetString());

        var page = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/products?q=bag2-brn");
        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal(8900, item.GetProperty("priceMin").GetInt64());
        Assert.Equal(12, item.GetProperty("stock").GetInt32());
    }

    [Fact]
    public async Task Editing_replaces_variants_and_keeps_skus_and_handles_unique()
    {
        var (owner, _) = await OwnerAsync();
        var bag = await (await owner.PostAsync("/api/v1/products", Bag())).Content.ReadFromJsonAsync<JsonElement>();
        await owner.PostAsync("/api/v1/products", Bag("Travel Bag", "TRV"));
        var id = bag.GetProperty("id").GetString();
        var black = bag.GetProperty("variants")[0].GetProperty("id").GetString();

        var edit = new
        {
            title = "Classic Leather Bag",
            optionNames = new[] { "Colour" },
            variants = new object[]
            {
                new { id = black, sku = "BAG-BLK", option1 = "Black", price = 9900, trackInventory = true, stock = 3 },
                new { sku = "BAG-RED", option1 = "Red", price = 9900 },
            },
        };
        var updated = await PutAsync(owner, $"/api/v1/products/{id}", edit);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var variants = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("variants");
        Assert.Equal(["BAG-BLK", "BAG-RED"], variants.EnumerateArray().Select(v => v.GetProperty("sku").GetString()));
        Assert.Equal(black, variants[0].GetProperty("id").GetString());

        var stolen = await PutAsync(owner, $"/api/v1/products/{id}", new
        {
            title = "Classic Leather Bag",
            variants = new object[] { new { sku = "TRV-BLK", price = 100 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        Assert.Equal("catalog.sku_taken", await AuthFlowTests.CodeOf(stolen));

        var rename = await PutAsync(owner, $"/api/v1/products/{id}", new { title = "x", handle = "new-handle", variants = new object[] { new { price = 1 } } });
        Assert.Equal("catalog.handle_fixed", await AuthFlowTests.CodeOf(rename));
    }

    [Fact]
    public async Task Invalid_products_are_refused_field_by_field()
    {
        var (owner, _) = await OwnerAsync();
        object[] invalid =
        [
            new { title = "", variants = new object[] { new { price = 100 } } },
            new { title = "No variants", variants = Array.Empty<object>() },
            new { title = "Negative", variants = new object[] { new { price = -1 } } },
            new { title = "Sale", variants = new object[] { new { price = 1000, compareAtPrice = 900 } } },
            new { title = "Currency", variants = new object[] { new { price = 1, currency = "euro" } } },
            new { title = "Missing option", optionNames = new[] { "Size" }, variants = new object[] { new { price = 1 } } },
            new { title = "Extra option", variants = new object[] { new { price = 1, option1 = "M" } } },
            new { title = "Same options", optionNames = new[] { "Size" }, variants = new object[] { new { price = 1, option1 = "M" }, new { price = 2, option1 = "m" } } },
            new { title = "Four options", optionNames = new[] { "A", "B", "C", "D" }, variants = new object[] { new { price = 1 } } },
            new { title = "Bad status", status = "deleted", variants = new object[] { new { price = 1 } } },
            new { title = "Same SKU", variants = new object[] { new { price = 1, sku = "X", option1 = (string?)null }, new { price = 1, sku = "x" } } },
        ];

        foreach (var body in invalid)
        {
            var response = await owner.PostAsync("/api/v1/products", body);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} → {response.StatusCode}");
        }
    }

    [Fact]
    public async Task Members_read_the_catalog_and_only_owners_and_admins_change_it()
    {
        var (owner, _) = await OwnerAsync();
        var bag = await (await owner.PostAsync("/api/v1/products", Bag())).Content.ReadFromJsonAsync<JsonElement>();
        var member = await MemberOfAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await member.Http.GetAsync("/api/v1/products")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.Http.GetAsync($"/api/v1/products/{bag.GetProperty("id").GetString()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/v1/products", Bag("Member bag", "MB"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(member, $"/api/v1/products/{bag.GetProperty("id").GetString()}", Bag())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(member, "preview", ShopifyFile)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Http.GetAsync("/api/v1/products/import/template")).StatusCode);
    }

    [Fact]
    public async Task Another_workspace_gets_not_found_and_the_database_blocks_it_too()
    {
        var (owner, workspaceA) = await OwnerAsync();
        var (other, workspaceB) = await OwnerAsync("Bottega Blu");
        var bag = await (await owner.PostAsync("/api/v1/products", Bag())).Content.ReadFromJsonAsync<JsonElement>();
        var id = bag.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync($"/api/v1/products/{id}")).StatusCode);
        Assert.Equal("catalog.product_not_found", await AuthFlowTests.CodeOf(await PutAsync(other, $"/api/v1/products/{id}", Bag())));
        Assert.Empty((await other.Http.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetProperty("items").EnumerateArray());
        // The same SKU is fine in another workspace.
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsync("/api/v1/products", Bag())).StatusCode);

        await using var connection = new NpgsqlConnection(fixture.Database.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @b, false)", connection))
        {
            setTenant.Parameters.AddWithValue("b", workspaceB);
            await setTenant.ExecuteNonQueryAsync();
        }

        await using var count = new NpgsqlCommand("SELECT count(*) FROM commerce.product_variants WHERE tenant_id = @a", connection);
        count.Parameters.AddWithValue("a", Guid.Parse(workspaceA));
        Assert.Equal(0L, await count.ExecuteScalarAsync());
    }

    private const string ShopifyFile =
        "Handle,Title,Body (HTML),Vendor,Product Category,Type,Tags,Published,Option1 Name,Option1 Value,Option2 Name,Option2 Value,Variant SKU,Variant Grams,Variant Inventory Tracker,Variant Inventory Qty,Variant Inventory Policy,Variant Price,Variant Compare At Price,Variant Requires Shipping,Variant Barcode,Image Src,Cost per item,Status\n" +
        "linen-shirt,Linen Shirt,\"<p>Light <strong>linen</strong>,<br>made in Italy &amp; washed.</p>\",Almanaam,Shirts,Shirt,\"summer, linen\",TRUE,Size,S,Colour,White,SHIRT-S-W,250,shopify,5,deny,49.00,59.00,TRUE,8001234567890,https://cdn.example/1.jpg,20.00,active\n" +
        "linen-shirt,,,,,,,,,M,,White,SHIRT-M-W,250,shopify,0,continue,49.00,,TRUE,,,20.00,\n" +
        "linen-shirt,,,,,,,,,,,,,,,,,,,,,https://cdn.example/2.jpg,,\n" +
        "gift-card,Gift Card,,Almanaam,,Gift,,FALSE,Title,Default Title,,,GIFT-25,0,,,deny,25.00,25.00,FALSE,,,,\n";

    [Fact]
    public async Task A_shopify_export_previews_then_imports_and_reimports_as_updates()
    {
        var (owner, workspace) = await OwnerAsync();

        var preview = await ReportAsync(await UploadAsync(owner, "preview", ShopifyFile));
        Assert.False(preview.GetProperty("applied").GetBoolean());
        Assert.Empty(preview.GetProperty("issues").EnumerateArray());
        AssertCounts(preview, productsCreated: 2, productsUpdated: 0, variantsCreated: 3, variantsUpdated: 0);
        Assert.Empty((await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetProperty("items").EnumerateArray());

        var applied = await UploadAsync(owner, "apply", ShopifyFile);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.True((await ReportAsync(applied)).GetProperty("applied").GetBoolean());

        var list = (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetProperty("items");
        Assert.Equal(2, list.GetArrayLength());
        var shirtId = list.EnumerateArray().Single(p => p.GetProperty("handle").GetString() == "linen-shirt").GetProperty("id").GetString();
        var shirt = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{shirtId}");
        Assert.Equal("Light linen,\nmade in Italy & washed.", shirt.GetProperty("description").GetString());
        Assert.Equal(["Size", "Colour"], shirt.GetProperty("optionNames").EnumerateArray().Select(o => o.GetString()));
        var small = shirt.GetProperty("variants")[0];
        Assert.Equal(4900, small.GetProperty("price").GetInt64());
        Assert.Equal(5900, small.GetProperty("compareAtPrice").GetInt64());
        Assert.Equal(2000, small.GetProperty("cost").GetInt64());
        Assert.Equal("8001234567890", small.GetProperty("barcode").GetString());
        Assert.True(shirt.GetProperty("variants")[1].GetProperty("available").GetBoolean());

        var gift = list.EnumerateArray().Single(p => p.GetProperty("handle").GetString() == "gift-card");
        Assert.Equal("draft", gift.GetProperty("status").GetString());

        // Re-import with new stock: the same products and variants are updated, nothing duplicated.
        var reimport = await ReportAsync(await UploadAsync(owner, "apply", ShopifyFile.Replace(",shopify,5,", ",shopify,7,", StringComparison.Ordinal)));
        AssertCounts(reimport, productsCreated: 0, productsUpdated: 2, variantsCreated: 0, variantsUpdated: 3);
        var restocked = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{shirtId}");
        Assert.Equal(7, restocked.GetProperty("variants")[0].GetProperty("stock").GetInt32());

        // The AI sees active products only, and never the cost.
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalog>();
        var found = Assert.Single(await catalog.SearchAsync("linen", 5, CancellationToken.None));
        Assert.Equal(["S", "White"], found.Variants[0].OptionValues);
        Assert.Empty(await catalog.SearchAsync("gift", 5, CancellationToken.None));
        Assert.DoesNotContain(typeof(CatalogVariant).GetProperties(), p => p.Name.Contains("Cost", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Problems_are_reported_by_line_and_column_and_nothing_is_saved()
    {
        var (owner, _) = await OwnerAsync();
        await owner.PostAsync("/api/v1/products", Bag());
        var file =
            "Handle,Title,Option1 Name,Option1 Value,Variant SKU,Variant Price,Variant Inventory Qty\n" +
            "good,Good Product,,,GOOD-1,10.00,\n" +
            "no-price,No Price,,,NP-1,,\n" +
            "bad-number,Bad Number,,,BN-1,ten,abc\n" +
            "steals,Steals A Sku,,,BAG-BLK,5.00,\n" +
            "twice,Twice,Size,S,GOOD-1,5.00,\n";

        var preview = await ReportAsync(await UploadAsync(owner, "preview", file));
        var issues = preview.GetProperty("issues").EnumerateArray()
            .Select(i => (i.GetProperty("line").GetInt32(), i.GetProperty("column").GetString(), i.GetProperty("code").GetString()))
            .ToList();
        Assert.Contains((3, "Variant Price", "required"), issues);
        Assert.Contains((4, "Variant Price", "invalid_number"), issues);
        Assert.Contains((4, "Variant Inventory Qty", "invalid_number"), issues);
        Assert.Contains((5, "Variant SKU", "sku_taken"), issues);
        Assert.Contains((6, "Variant SKU", "duplicate_sku"), issues);

        var apply = await UploadAsync(owner, "apply", file);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, apply.StatusCode);
        var titles = (await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetProperty("items")
            .EnumerateArray().Select(p => p.GetProperty("title").GetString());
        Assert.Equal(["Classic Leather Bag"], titles);
    }

    [Fact]
    public async Task Excel_files_from_italy_with_semicolons_and_decimal_commas_work()
    {
        var (owner, _) = await OwnerAsync();
        var file = "﻿Handle;Title;Variant SKU;Variant Price\r\nborsa;Borsa in pelle;BORSA-1;89,50\r\n";

        var report = await ReportAsync(await UploadAsync(owner, "apply", file));
        Assert.True(report.GetProperty("applied").GetBoolean());
        var item = Assert.Single((await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/products")).GetProperty("items").EnumerateArray());
        Assert.Equal(8950, item.GetProperty("priceMin").GetInt64());
    }

    [Fact]
    public async Task Only_real_csv_text_is_accepted()
    {
        var (owner, _) = await OwnerAsync();

        var xlsx = await UploadBytesAsync(owner, "preview", [(byte)'P', (byte)'K', 3, 4, 20, 0, 6, 0], "products.csv");
        Assert.Equal("catalog.import_not_csv", await AuthFlowTests.CodeOf(xlsx));
        var binary = await UploadBytesAsync(owner, "preview", [0xFF, 0xFE, 0x00, 0x41], "products.csv");
        Assert.Equal("catalog.import_not_csv", await AuthFlowTests.CodeOf(binary));
        var empty = await UploadBytesAsync(owner, "preview", [], "products.csv");
        Assert.Equal("catalog.import_no_file", await AuthFlowTests.CodeOf(empty));
        var huge = await UploadBytesAsync(owner, "preview", Encoding.UTF8.GetBytes(new string('a', 2 * 1024 * 1024 + 10)), "products.csv");
        Assert.True(huge.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, huge.StatusCode.ToString());

        var tooMany = "Handle,Title,Variant Price\n" + string.Concat(Enumerable.Range(0, 5001).Select(i => $"p{i},P,1.00\n"));
        var report = await ReportAsync(await UploadAsync(owner, "preview", tooMany));
        Assert.Equal("too_many_rows", report.GetProperty("issues")[0].GetProperty("code").GetString());

        var template = await owner.Http.GetAsync("/api/v1/products/import/template");
        Assert.Equal("text/csv", template.Content.Headers.ContentType?.MediaType);
        var lines = (await template.Content.ReadAsStringAsync()).Split("\r\n");
        Assert.StartsWith("Handle,Title,Body (HTML)", lines[0], StringComparison.Ordinal);
        // The template itself imports cleanly.
        var round = await ReportAsync(await UploadAsync(owner, "preview", string.Join("\n", lines)));
        Assert.Empty(round.GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task The_ai_search_finds_products_by_any_word_best_match_first()
    {
        var (owner, workspace) = await OwnerAsync();
        var (other, _) = await OwnerAsync("Bottega Blu");
        await CreateAsync(owner, "Borsa a tracolla in pelle", "active", "Borsa cucita a mano.", "Nero", "Marrone");
        await CreateAsync(owner, "Zaino in pelle", "active", "Con tasca per laptop.", "Nero");
        await CreateAsync(owner, "Sciarpa di seta", "active", null, "Rosso");
        await CreateAsync(owner, "Borsa shopper in tela", "draft", null, "Nero");
        await CreateAsync(other, "Borsa di un altro negozio", "active", null, "Nero");

        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var catalog = scope.ServiceProvider.GetRequiredService<ICatalog>();
        async Task<List<string>> Titles(string query) =>
            (await catalog.SearchAsync(query, 5, CancellationToken.None)).Select(p => p.Title).ToList();

        // The whole phrase isn't in any title; its words are. This found nothing with phrase search.
        Assert.Equal("Borsa a tracolla in pelle", (await Titles("borsa a tracolla nera"))[0]);
        // Word forms: plural, and a colour that is only an option value.
        Assert.Contains("Borsa a tracolla in pelle", await Titles("borse"));
        Assert.Contains("Sciarpa di seta", await Titles("sciarpe rosse"));
        Assert.Contains("Zaino in pelle", await Titles("nera"));
        // Also the description; the more words match, the higher.
        Assert.Equal(["Zaino in pelle"], await Titles("laptop"));
        Assert.Equal("Zaino in pelle", (await Titles("zaino in pelle"))[0]);

        // Never drafts or another workspace's products; nothing for words that match nothing.
        Assert.DoesNotContain("Borsa shopper in tela", await Titles("borsa shopper"));
        Assert.DoesNotContain("Borsa di un altro negozio", await Titles("borsa negozio"));
        Assert.Empty(await Titles("orologio"));
        // Pattern characters are plain text, not wildcards (SEC-01).
        Assert.Empty(await Titles("%"));
        Assert.Empty(await Titles("_"));
    }

    private static async Task CreateAsync(ApiClient owner, string title, string status, string? description, params string[] colours) =>
        (await owner.PostAsync("/api/v1/products", new
        {
            title,
            status,
            description,
            optionNames = ColourOption,
            variants = colours.Select(c => new { option1 = c, price = 1000 }).ToArray(),
        })).EnsureSuccessStatusCode();

    private async Task<(ApiClient Client, string Workspace)> OwnerAsync(string business = "Almanaam")
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), business)).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetString()!);
    }

    private async Task<ApiClient> MemberOfAsync(ApiClient owner)
    {
        var email = AuthFixture.NewEmail();
        await owner.PostAsync("/api/v1/team/invitations", new { email, role = "member" });
        var link = System.Text.RegularExpressions.Regex.Match(Api.Emails.LastTo(email).TextBody, @"https://\S+accept-invitation\?token=\S+").Value;
        var token = HttpUtility.ParseQueryString(new Uri(link).Query)["token"];
        var member = Api.CreateApiClient();
        (await member.PostAsync("/api/v1/invitations/accept", new { token, name = "Luca", password = "girasole tramonto barca verde" })).EnsureSuccessStatusCode();
        return member;
    }

    private static async Task<HttpResponseMessage> PutAsync(ApiClient client, string path, object body)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsJsonAsync(path, body);
    }

    private static Task<HttpResponseMessage> UploadAsync(ApiClient client, string step, string csv) =>
        UploadBytesAsync(client, step, Encoding.UTF8.GetBytes(csv), "products.csv");

    private static async Task<HttpResponseMessage> UploadBytesAsync(ApiClient client, string step, byte[] bytes, string name)
    {
        await client.RefreshCsrfAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", name);
        return await client.Http.PostAsync($"/api/v1/products/import/{step}", form);
    }

    private static async Task<JsonElement> ReportAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static void AssertCounts(JsonElement report, int productsCreated, int productsUpdated, int variantsCreated, int variantsUpdated)
    {
        var counts = report.GetProperty("counts");
        Assert.Equal(
            (productsCreated, productsUpdated, variantsCreated, variantsUpdated),
            (counts.GetProperty("productsCreated").GetInt32(), counts.GetProperty("productsUpdated").GetInt32(),
                counts.GetProperty("variantsCreated").GetInt32(), counts.GetProperty("variantsUpdated").GetInt32()));
    }
}
