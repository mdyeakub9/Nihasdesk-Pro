using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>
/// Its own database: these tests leave many conversations due for the AI and make the fake web fail,
/// which must not reach other tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MediaTestsDatabase : ICollectionFixture<AuthFixture>
{
    public const string Name = "media";
}

/// <summary>Customer media from webhook to dashboard against a fake Meta (Issue #84, ADR 0031, SEC-04/05/06).</summary>
[Collection(MediaTestsDatabase.Name)]
public sealed partial class MediaTests(AuthFixture fixture)
{
    private const string Webhook = "/api/v1/webhooks/meta";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task A_photo_is_kept_without_its_metadata_and_shown_in_the_conversation()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("photo.jpg");
        Api.Web.Pages[url] = ("application/octet-stream", MediaImageTests.JpegWithExif(40, 20, "GPS-45.4642N"));

        var conversation = await ReceiveAsync(pageId, [("image", url)]);

        var pending = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("pending", pending.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("url").ValueKind);

        await Api.DownloadMediaAsync();

        var saved = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("saved", saved.GetProperty("status").GetString());
        Assert.Equal("image", saved.GetProperty("kind").GetString());
        Assert.Equal("image/jpeg", saved.GetProperty("contentType").GetString());
        Assert.Equal(20, saved.GetProperty("width").GetInt32());
        Assert.Equal(40, saved.GetProperty("height").GetInt32());
        // The channel's link stays with us until the download and is never shown.
        Assert.DoesNotContain("fbcdn", saved.GetRawText(), StringComparison.Ordinal);

        var message = (await MessagesAsync(owner, conversation)).Single();
        Assert.Equal(string.Empty, message.GetProperty("text").GetString());
        Assert.Equal("[image]", await PreviewAsync(owner, conversation));

        var file = await owner.Http.GetAsync(saved.GetProperty("url").GetString());
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/jpeg", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", string.Join(',', file.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("inline", file.Content.Headers.ContentDisposition!.DispositionType);
        Assert.DoesNotContain("GPS-45.4642N", Encoding.ASCII.GetString(await file.Content.ReadAsByteArrayAsync()), StringComparison.Ordinal);

        var thumbnail = await owner.Http.GetAsync(saved.GetProperty("thumbnailUrl").GetString());
        Assert.Equal(HttpStatusCode.OK, thumbnail.StatusCode);
    }

    [Fact]
    public async Task Text_and_several_attachments_of_one_message_stay_together_in_order()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var first = Cdn("one.png");
        var second = Cdn("two.png");
        Api.Web.Pages[first] = ("image/png", MediaImageTests.Png(10, 10));
        Api.Web.Pages[second] = ("image/png", MediaImageTests.Png(20, 10));

        var conversation = await ReceiveAsync(pageId, [("image", first), ("image", second)], text: "Avete questi?");
        await Api.DownloadMediaAsync();

        var message = (await MessagesAsync(owner, conversation)).Single();
        Assert.Equal("Avete questi?", message.GetProperty("text").GetString());
        Assert.Equal([10, 20], message.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("width").GetInt32()));
        Assert.Equal("Avete questi?", await PreviewAsync(owner, conversation));
    }

    [Theory]
    [InlineData("https://evil.example/photo.jpg")]
    [InlineData("https://evilfbcdn.net/photo.jpg")]
    [InlineData("https://fbcdn.net.evil.example/photo.jpg")]
    [InlineData("http://scontent.xx.fbcdn.net/photo.jpg")]
    [InlineData("https://private.fbcdn.net/photo.jpg")]
    [InlineData("not a link")]
    public async Task Links_outside_Metas_public_cdn_are_never_fetched(string url)
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var before = Api.Web.Requested.Count;

        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();

        var attachment = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("failed", attachment.GetProperty("status").GetString());
        Assert.Equal("source_not_allowed", attachment.GetProperty("failureCode").GetString());
        Assert.Equal(before, Api.Web.Requested.Count);
    }

    [Fact]
    public async Task A_redirect_away_from_the_cdn_is_not_followed()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("moved.jpg");
        var target = $"https://evil.example/{Guid.NewGuid():N}.jpg";
        Api.Web.Redirects[url] = target;

        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();

        Assert.Equal("source_not_allowed", (await AttachmentsAsync(owner, conversation)).Single().GetProperty("failureCode").GetString());
        Assert.DoesNotContain(target, Api.Web.Requested);
    }

    [Fact]
    public async Task A_program_renamed_jpg_is_refused_and_not_kept()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("holiday.jpg");
        Api.Web.Pages[url] = ("image/jpeg", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
        var filesBefore = FileCount();

        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();

        var attachment = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("failed", attachment.GetProperty("status").GetString());
        Assert.Equal("type_not_allowed", attachment.GetProperty("failureCode").GetString());
        Assert.Equal(JsonValueKind.Null, attachment.GetProperty("url").ValueKind);
        Assert.Equal(filesBefore, FileCount());
    }

    [Fact]
    public async Task A_broken_image_is_refused()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("broken.jpg");
        Api.Web.Pages[url] = ("image/jpeg", [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);

        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();

        Assert.Equal("type_not_allowed", (await AttachmentsAsync(owner, conversation)).Single().GetProperty("failureCode").GetString());
    }

    [Fact]
    public async Task Media_over_the_limit_is_marked_too_large_and_not_kept()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("huge.jpg");
        Api.Web.Pages[url] = ("image/jpeg", new byte[(10 * 1024 * 1024) + 1]);
        var filesBefore = FileCount();

        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();

        var attachment = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("too_large", attachment.GetProperty("status").GetString());
        Assert.Equal("too_large", attachment.GetProperty("failureCode").GetString());
        Assert.Equal(filesBefore, FileCount());
    }

    [Fact]
    public async Task A_file_announced_as_a_file_but_too_big_for_an_image_is_too_large()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("big.png");
        byte[] big = [.. MediaImageTests.Png(10, 10), .. new byte[(10 * 1024 * 1024) + 1]];
        Api.Web.Pages[url] = ("application/octet-stream", big);

        var conversation = await ReceiveAsync(pageId, [("file", url)]);
        await Api.DownloadMediaAsync();

        Assert.Equal("too_large", (await AttachmentsAsync(owner, conversation)).Single().GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_repeated_webhook_downloads_once()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("again.png");
        Api.Web.Pages[url] = ("image/png", MediaImageTests.Png(10, 10));
        var psid = NewId();
        var mid = "m-" + Guid.NewGuid().ToString("N");

        var conversation = await ReceiveAsync(pageId, [("image", url)], psid: psid, mid: mid);
        await ReceiveAsync(pageId, [("image", url)], psid: psid, mid: mid);
        await Api.DownloadMediaAsync();
        await ReceiveAsync(pageId, [("image", url)], psid: psid, mid: mid);
        await Api.DownloadMediaAsync();

        Assert.Single(await AttachmentsAsync(owner, conversation));
        Assert.Single(Api.Web.Requested, r => r == url);
    }

    [Fact]
    public async Task An_expired_link_fails_at_once_and_says_so()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("gone.jpg");

        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();

        var attachment = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("failed", attachment.GetProperty("status").GetString());
        Assert.Equal("expired", attachment.GetProperty("failureCode").GetString());
        Assert.Single(Api.Web.Requested, r => r == url);
    }

    [Fact]
    public async Task A_message_without_a_link_says_the_media_is_gone()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();

        var conversation = await ReceiveAsync(pageId, [("video", null)]);
        await Api.DownloadMediaAsync();

        Assert.Equal("expired", (await AttachmentsAsync(owner, conversation)).Single().GetProperty("failureCode").GetString());
    }

    [Fact]
    public async Task Other_attachment_types_still_tell_the_team_something_came()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();

        var conversation = await ReceiveAsync(pageId, [("location", "https://maps.example/x")]);

        var message = (await MessagesAsync(owner, conversation)).Single();
        Assert.Equal("[attachment]", message.GetProperty("text").GetString());
        Assert.Empty(message.GetProperty("attachments").EnumerateArray());
    }

    [Fact]
    public async Task A_voice_note_in_an_mp4_is_audio_and_plays_in_the_browser()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("audioclip.mp4");
        Api.Web.Pages[url] = ("video/mp4", Convert.FromHexString("00000018667479706973" + "6F6D0000000069736F6D"));

        var conversation = await ReceiveAsync(pageId, [("audio", url)]);
        await Api.DownloadMediaAsync();

        var attachment = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("audio", attachment.GetProperty("kind").GetString());
        Assert.Equal("audio/mp4", attachment.GetProperty("contentType").GetString());
        var file = await owner.Http.GetAsync(attachment.GetProperty("url").GetString());
        Assert.Equal("inline", file.Content.Headers.ContentDisposition!.DispositionType);
    }

    [Fact]
    public async Task A_pdf_only_downloads_under_a_cleaned_name()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("..%2F..%2Fmy%20<order>.exe");
        Api.Web.Pages[new Uri(url).AbsoluteUri] = ("application/pdf", "%PDF-1.7 made up"u8.ToArray());

        var conversation = await ReceiveAsync(pageId, [("file", url)]);
        await Api.DownloadMediaAsync();

        var attachment = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("file", attachment.GetProperty("kind").GetString());
        Assert.Equal("application/pdf", attachment.GetProperty("contentType").GetString());
        var name = attachment.GetProperty("fileName").GetString()!;
        Assert.EndsWith(".pdf", name, StringComparison.Ordinal);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('<', name);
        Assert.DoesNotContain("exe", name, StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, attachment.GetProperty("thumbnailUrl").ValueKind);

        var file = await owner.Http.GetAsync(attachment.GetProperty("url").GetString());
        Assert.Equal("attachment", file.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", string.Join(',', file.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task A_network_problem_is_tried_again_and_then_given_up()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("flaky.png");
        Api.Web.Pages[url] = ("image/png", MediaImageTests.Png(10, 10));
        Api.Web.FailWith = HttpStatusCode.ServiceUnavailable;
        try
        {
            var conversation = await ReceiveAsync(pageId, [("image", url)]);
            await Api.DownloadMediaAsync();
            Assert.Equal("pending", (await AttachmentsAsync(owner, conversation)).Single().GetProperty("status").GetString());

            foreach (var wait in new[] { 31, 121, 601 })
            {
                Api.Clock.Advance(TimeSpan.FromSeconds(wait));
                await Api.DownloadMediaAsync();
            }

            var attachment = (await AttachmentsAsync(owner, conversation)).Single();
            Assert.Equal("failed", attachment.GetProperty("status").GetString());
            Assert.Equal("network", attachment.GetProperty("failureCode").GetString());
        }
        finally
        {
            Api.Web.FailWith = null;
        }
    }

    [Fact]
    public async Task A_network_problem_that_passes_ends_with_the_file()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("back.png");
        Api.Web.Pages[url] = ("image/png", MediaImageTests.Png(10, 10));
        Api.Web.FailWith = HttpStatusCode.TooManyRequests;
        JsonElement attachment;
        try
        {
            var conversation = await ReceiveAsync(pageId, [("image", url)]);
            await Api.DownloadMediaAsync();
            Api.Web.FailWith = null;
            Api.Clock.Advance(TimeSpan.FromSeconds(31));
            await Api.DownloadMediaAsync();
            attachment = (await AttachmentsAsync(owner, conversation)).Single();
        }
        finally
        {
            Api.Web.FailWith = null;
        }

        Assert.Equal("saved", attachment.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Another_workspaces_media_is_not_found_and_strangers_are_refused()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("private.png");
        Api.Web.Pages[url] = ("image/png", MediaImageTests.Png(10, 10));
        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();
        var attachment = (await AttachmentsAsync(owner, conversation)).Single();
        var link = attachment.GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await owner.Http.GetAsync(link)).StatusCode);

        var (other, _) = await ConnectedOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync(link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.GetAsync(link + "?variant=thumbnail")).StatusCode);

        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync(link)).StatusCode);
    }

    [Fact]
    public async Task The_endpoint_refuses_unknown_variants_and_media_that_is_not_saved()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var conversation = await ReceiveAsync(pageId, [("image", Cdn("wait.png"))]);
        var id = (await AttachmentsAsync(owner, conversation)).Single().GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/v1/media/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Http.GetAsync($"/api/v1/media/{id}?variant=ai")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/v1/media/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task The_ai_hands_off_on_media_alone()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("ai-photo.png");
        Api.Web.Pages[url] = ("image/png", MediaImageTests.Png(10, 10));

        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await RunAllAiAsync();

        Assert.Contains(await MessagesAsync(owner, conversation), IsMediaHandoff);
        Assert.Equal("human", await ResponderAsync(owner, conversation));
    }

    [Fact]
    public async Task The_ai_hands_off_on_a_photo_followed_by_a_question_or_sent_with_one()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var psid = NewId();
        var conversation = await ReceiveAsync(pageId, [("image", Cdn("then-text.png"))], psid: psid);
        await ReceiveAsync(pageId, [], text: "Avete questa?", psid: psid);
        await RunAllAiAsync();
        Assert.Contains(await MessagesAsync(owner, conversation), IsMediaHandoff);

        var together = await ReceiveAsync(pageId, [("image", Cdn("with-text.png"))], text: "Avete questa?");
        await RunAllAiAsync();
        Assert.Contains(await MessagesAsync(owner, together), IsMediaHandoff);
    }

    private async Task RunAllAiAsync()
    {
        while (await Api.RunAiAsync() > 0)
        {
        }
    }

    private static bool IsMediaHandoff(JsonElement message) =>
        message.GetProperty("internal").GetBoolean()
        && message.GetProperty("text").GetString()!.Contains("photo, voice note or file", StringComparison.Ordinal);

    private int FileCount() =>
        Directory.Exists(Api.FilesFolder) ? Directory.GetFiles(Api.FilesFolder, "*", SearchOption.AllDirectories).Length : 0;

    private static string Cdn(string name) => $"https://scontent.xx.fbcdn.net/v/{Guid.NewGuid():N}/{name}?oe=abc";

    private static string NewId() => Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(CultureInfo.InvariantCulture);

    private async Task<string> ReceiveAsync(
        string pageId, (string Type, string? Url)[] attachments, string? text = null, string? psid = null, string? mid = null)
    {
        psid ??= NewId();
        mid ??= "m-" + Guid.NewGuid().ToString("N");
        var message = new Dictionary<string, object> { ["mid"] = mid };
        if (text is not null)
        {
            message["text"] = text;
        }

        if (attachments.Length > 0)
        {
            message["attachments"] = attachments.Select(a => new { type = a.Type, payload = new { url = a.Url } }).ToArray();
        }

        var body = JsonSerializer.Serialize(new
        {
            @object = "page",
            entry = new[]
            {
                new
                {
                    id = pageId,
                    time = Api.Clock.GetUtcNow().ToUnixTimeMilliseconds(),
                    messaging = new[]
                    {
                        new
                        {
                            sender = new { id = psid },
                            recipient = new { id = pageId },
                            timestamp = Api.Clock.GetUtcNow().ToUnixTimeMilliseconds(),
                            message,
                        },
                    },
                },
            },
        });

        using var meta = Api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var request = new HttpRequestMessage(HttpMethod.Post, Webhook) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(
            "X-Hub-Signature-256",
            "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(FakeMetaGraphHandler.AppSecret), Encoding.UTF8.GetBytes(body))));
        Assert.Equal(HttpStatusCode.OK, (await meta.SendAsync(request)).StatusCode);
        await Api.ProcessMetaWebhooksAsync();

        return await ConversationOfAsync(pageId, psid);
    }

    private readonly Dictionary<string, ApiClient> _owners = [];

    private readonly Dictionary<string, string> _conversations = [];

    private async Task<string> ConversationOfAsync(string pageId, string psid)
    {
        var key = pageId + "/" + psid;
        if (_conversations.TryGetValue(key, out var known))
        {
            return known;
        }

        var seen = _conversations.Values.ToHashSet();
        var list = await _owners[pageId].Http.GetFromJsonAsync<JsonElement>("/api/v1/conversations?limit=100");
        var id = list.GetProperty("items").EnumerateArray().Select(c => c.GetProperty("id").GetString()!).First(i => !seen.Contains(i));
        _conversations[key] = id;
        return id;
    }

    private static async Task<List<JsonElement>> MessagesAsync(ApiClient client, string conversation)
    {
        var page = await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversation}/messages");
        return [.. page.GetProperty("items").EnumerateArray()];
    }

    private static async Task<List<JsonElement>> AttachmentsAsync(ApiClient client, string conversation) =>
        [.. (await MessagesAsync(client, conversation)).SelectMany(m => m.GetProperty("attachments").EnumerateArray())];

    private static async Task<string?> PreviewAsync(ApiClient client, string conversation) =>
        (await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversation}")).GetProperty("conversation").GetProperty("preview").GetString();

    private static async Task<string?> ResponderAsync(ApiClient client, string conversation) =>
        (await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversation}")).GetProperty("conversation").GetProperty("responder").GetString();

    private async Task<(ApiClient Owner, string PageId)> ConnectedOwnerAsync()
    {
        var pageId = NewId();
        Api.Meta.Pages.Clear();
        Api.Meta.Pages.Add((pageId, "Page " + pageId, "page-token-" + pageId));
        var client = Api.CreateApiClient(followRedirects: false);
        await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Almanaam");
        var start = await (await client.PostAsync("/api/v1/channels/meta/connect")).Content.ReadFromJsonAsync<JsonElement>();
        var state = HttpUtility.ParseQueryString(new Uri(start.GetProperty("url").GetString()!).Query)["state"]!;
        var callback = await client.Http.GetAsync($"/api/v1/channels/meta/callback?code=fake-code&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        _owners[pageId] = client;
        return (client, pageId);
    }
}
