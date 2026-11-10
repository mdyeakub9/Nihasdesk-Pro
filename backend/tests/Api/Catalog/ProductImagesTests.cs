using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Api.Tests.Inbox;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.TestSupport;
using SkiaSharp;

namespace Nihasdesk.Api.Tests.Catalog;

/// <summary>Product photos in the editor and their signed public links (Issue #86, ADR 0031 §7, SEC-04/06/10).</summary>
[Collection(AuthTests.Name)]
public sealed class ProductImagesTests(AuthFixture fixture)
{
    private static readonly string[] ColourOption = ["Colore"];

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task An_uploaded_photo_is_re_encoded_without_metadata_and_shown_to_the_team()
    {
        var (owner, product) = await ProductAsync();

        var response = await UploadAsync(owner, product.Id, MediaImageTests.JpegWithExif(60, 30, "GPS-45.4642N-9.1900E"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var image = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("saved", image.GetProperty("status").GetString());
        Assert.Equal(1, image.GetProperty("position").GetInt32());
        // The EXIF "rotate 90°" is applied before the EXIF is dropped.
        Assert.Equal(30, image.GetProperty("width").GetInt32());
        Assert.Equal(60, image.GetProperty("height").GetInt32());

        var card = await owner.Http.GetAsync(image.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);
        Assert.Equal("image/jpeg", card.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", card.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("private", card.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("GPS-45.4642N", Encoding.ASCII.GetString(await card.Content.ReadAsByteArrayAsync()), StringComparison.Ordinal);
        var thumbnail = await owner.Http.GetAsync(image.GetProperty("thumbnailUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);

        var detail = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{product.Id}");
        Assert.Equal(image.GetProperty("id").GetString(), detail.GetProperty("images")[0].GetProperty("id").GetString());
        var list = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/products");
        var item = list.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetString() == product.Id);
        Assert.Equal(image.GetProperty("thumbnailUrl").GetString(), item.GetProperty("thumbnailUrl").GetString());
    }

    [Fact]
    public async Task A_big_photo_is_kept_at_card_size()
    {
        var (owner, product) = await ProductAsync();

        var image = await (await UploadAsync(owner, product.Id, MediaImageTests.Png(2400, 1200))).Content.ReadFromJsonAsync<JsonElement>();

        var bytes = await owner.Http.GetByteArrayAsync(image.GetProperty("url").GetString());
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(1200, bitmap.Width);
        Assert.Equal(600, bitmap.Height);
    }

    [Theory]
    [InlineData("4D5A900003000000")] // a program renamed .jpg
    [InlineData("474946383961010001000000")] // GIF: not on the catalog's list
    [InlineData("3C73766720786D6C6E733D")] // <svg
    public async Task Anything_but_a_JPG_PNG_or_WebP_is_refused_and_nothing_is_kept(string hex)
    {
        var (owner, product) = await ProductAsync();
        var filesBefore = FileCount();

        var response = await UploadAsync(owner, product.Id, Convert.FromHexString(hex));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "catalog.image_type_not_allowed");
        Assert.Equal(filesBefore, FileCount());
    }

    [Fact]
    public async Task A_photo_over_10_MB_is_refused()
    {
        var (owner, product) = await ProductAsync();

        var response = await UploadAsync(owner, product.Id, new byte[(10 * 1024 * 1024) + 1]);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "catalog.image_too_large");
    }

    [Fact]
    public async Task A_product_has_at_most_8_photos()
    {
        var (owner, product) = await ProductAsync();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await UploadAsync(owner, product.Id, MediaImageTests.Png(8, 8))).StatusCode);
        }

        await AssertProblemAsync(await UploadAsync(owner, product.Id, MediaImageTests.Png(8, 8)), HttpStatusCode.BadRequest, "catalog.too_many_images");
    }

    [Fact]
    public async Task Photos_are_reordered_and_deleting_one_moves_the_rest_up_and_frees_its_variants()
    {
        var (owner, product) = await ProductAsync();
        var first = await UploadIdAsync(owner, product.Id);
        var second = await UploadIdAsync(owner, product.Id);
        var third = await UploadIdAsync(owner, product.Id);

        var order = await PutAsync(owner, $"/api/v1/products/{product.Id}/images/order", new { imageIds = new[] { third, first, second } });
        Assert.Equal(HttpStatusCode.OK, order.StatusCode);
        Assert.Equal([third, first, second], (await order.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Select(i => i.GetProperty("id").GetString()));
        // The new order must name every photo once.
        await AssertProblemAsync(
            await PutAsync(owner, $"/api/v1/products/{product.Id}/images/order", new { imageIds = new[] { third, first } }),
            HttpStatusCode.BadRequest,
            "catalog.image_order_mismatch");

        // The black one gets its own photo.
        var edited = await PutAsync(owner, $"/api/v1/products/{product.Id}", Body(product, ("Nero", first), ("Rosso", null)));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(first, (await edited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("variants")[0].GetProperty("imageId").GetString());

        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"/api/v1/products/{product.Id}/images/{first}")).StatusCode);

        var detail = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/products/{product.Id}");
        var images = detail.GetProperty("images").EnumerateArray().ToList();
        Assert.Equal([third, second], images.Select(i => i.GetProperty("id").GetString()));
        Assert.Equal([1, 2], images.Select(i => i.GetProperty("position").GetInt32()));
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("variants")[0].GetProperty("imageId").ValueKind);
    }

    [Fact]
    public async Task A_variant_cannot_use_a_photo_of_another_product_or_workspace()
    {
        var (owner, product) = await ProductAsync();
        var other = await AnotherProductAsync(owner);
        var otherPhoto = await UploadIdAsync(owner, other.Id);
        var (stranger, theirs) = await ProductAsync();
        var theirPhoto = await UploadIdAsync(stranger, theirs.Id);

        await AssertProblemAsync(
            await PutAsync(owner, $"/api/v1/products/{product.Id}", Body(product, ("Nero", otherPhoto), ("Rosso", null))),
            HttpStatusCode.NotFound,
            "catalog.image_not_found");
        await AssertProblemAsync(
            await PutAsync(owner, $"/api/v1/products/{product.Id}", Body(product, ("Nero", theirPhoto), ("Rosso", null))),
            HttpStatusCode.NotFound,
            "catalog.image_not_found");
        // Another workspace's photo can't be seen, moved or deleted either (SEC-06).
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/v1/products/{theirs.Id}/images/{theirPhoto}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(owner, theirs.Id, MediaImageTests.Png(8, 8))).StatusCode);
        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.DeleteAsync($"/api/v1/products/{theirs.Id}/images/{theirPhoto}")).StatusCode);
    }

    [Fact]
    public async Task Members_see_the_photos_but_only_owners_and_admins_change_them()
    {
        var (owner, product) = await ProductAsync();
        var photo = await UploadIdAsync(owner, product.Id);
        var member = await MemberOfAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await member.Http.GetAsync($"/api/v1/products/{product.Id}/images/{photo}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(member, product.Id, MediaImageTests.Png(8, 8))).StatusCode);
        await member.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Http.DeleteAsync($"/api/v1/products/{product.Id}/images/{photo}")).StatusCode);
        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync($"/api/v1/products/{product.Id}/images/{photo}")).StatusCode);
    }

    [Fact]
    public async Task The_signed_link_opens_the_card_photo_of_an_active_product_and_nothing_else()
    {
        var (owner, product) = await ProductAsync();
        var photo = await UploadIdAsync(owner, product.Id);
        var link = await PublicLinkAsync(product.Id, photo);
        Assert.StartsWith(NihasdeskApiFactory.PublicUrl + "/media/p/", link, StringComparison.Ordinal);
        var path = new Uri(link).AbsolutePath;

        using var stranger = Api.CreateClient();
        var opened = await stranger.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        Assert.Equal("image/jpeg", opened.Content.Headers.ContentType?.MediaType);
        Assert.Contains("public", opened.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        Assert.Equal(await owner.Http.GetByteArrayAsync($"/api/v1/products/{product.Id}/images/{photo}"), await opened.Content.ReadAsByteArrayAsync());

        // Any change to the token, a made-up one, or the link of a product taken off sale: all the same 404.
        var token = path["/media/p/".Length..];
        var changed = token[..10] + (token[10] == 'A' ? 'B' : 'A') + token[11..];
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/media/p/" + changed)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/media/p/" + new string('A', 99))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/media/p/x")).StatusCode);
        await PutAsync(owner, $"/api/v1/products/{product.Id}", Body(product, ("Nero", null), ("Rosso", null)) with { Status = "draft" });
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task The_signed_link_stops_working_after_7_days()
    {
        var (owner, product) = await ProductAsync();
        var photo = await UploadIdAsync(owner, product.Id);
        var path = new Uri(await PublicLinkAsync(product.Id, photo)).AbsolutePath;
        using var stranger = Api.CreateClient();

        Api.Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(path)).StatusCode);
        }
        finally
        {
            Api.Clock.Advance(-(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1)));
        }
    }

    /// <summary>The link Messenger cards get, made the way the renderer makes it.</summary>
    private readonly Dictionary<string, Guid> _workspaces = [];

    private async Task<string> PublicLinkAsync(string productId, string imageId)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(_workspaces[productId]);
        var links = scope.ServiceProvider.GetRequiredService<IProductImageLinks>();
        return (await links.CardUrlAsync(Guid.Parse(imageId), CancellationToken.None))!;
    }

    private sealed record CreatedProduct(string Id, List<string> Variants);

    private sealed record EditBody(string Title, string Status, string[] OptionNames, object[] Variants);

    private static EditBody Body(CreatedProduct product, params (string Option, string? ImageId)[] variants) => new(
        "Borsa Milano",
        "active",
        ColourOption,
        variants.Select((v, i) => (object)new { id = product.Variants[i], option1 = v.Option, price = 8900, imageId = v.ImageId }).ToArray());

    private async Task<(ApiClient Owner, CreatedProduct Product)> ProductAsync()
    {
        var owner = Api.CreateApiClient();
        var me = await (await owner.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Almanaam")).Content.ReadFromJsonAsync<JsonElement>();
        var product = await CreateAsync(owner, "Borsa Milano");
        _workspaces[product.Id] = Guid.Parse(me.GetProperty("workspace").GetProperty("id").GetString()!);
        return (owner, product);
    }

    private static Task<CreatedProduct> AnotherProductAsync(ApiClient owner) => CreateAsync(owner, "Borsa Roma");

    private static async Task<CreatedProduct> CreateAsync(ApiClient owner, string title)
    {
        var created = await owner.PostAsync("/api/v1/products", new
        {
            title,
            status = "active",
            optionNames = ColourOption,
            variants = new[] { new { option1 = "Nero", price = 8900 }, new { option1 = "Rosso", price = 8900 } },
        });
        created.EnsureSuccessStatusCode();
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return new CreatedProduct(
            body.GetProperty("id").GetString()!,
            body.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("id").GetString()!).ToList());
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

    private static async Task<string> UploadIdAsync(ApiClient owner, string productId)
    {
        var response = await UploadAsync(owner, productId, MediaImageTests.Jpeg(40, 30));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    internal static async Task<HttpResponseMessage> UploadAsync(ApiClient client, string productId, byte[] bytes)
    {
        await client.RefreshCsrfAsync();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        // What the browser says is never believed.
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "photo.jpg");
        return await client.Http.PostAsync($"/api/v1/products/{productId}/images", form);
    }

    private static async Task<HttpResponseMessage> PutAsync(ApiClient client, string path, object body)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsJsonAsync(path, body);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    private int FileCount() =>
        Directory.Exists(Api.FilesFolder) ? Directory.GetFiles(Api.FilesFolder, "*", SearchOption.AllDirectories).Length : 0;
}
