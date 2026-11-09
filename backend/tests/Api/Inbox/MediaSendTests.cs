using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>Issue #84 PR 3: the team sends a photo (ADR 0031 §6, SEC-04/43), retention (SEC-44) and erasure (SEC-45).</summary>
public sealed partial class MediaTests
{
    [Fact]
    public async Task The_team_sends_a_photo_without_its_metadata_through_the_Send_API()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var psid = NewId();
        var conversation = await ReceiveAsync(pageId, [], text: "Mi mandi una foto?", psid: psid);

        var response = await SendPhotoAsync(owner, conversation, MediaImageTests.JpegWithExif(60, 30, "GPS-45.4642N-9.1900E"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sent = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("agent", sent.GetProperty("author").GetString());
        Assert.Equal(string.Empty, sent.GetProperty("text").GetString());
        Assert.Equal("pending", sent.GetProperty("delivery").GetString());
        var photo = sent.GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal("saved", photo.GetProperty("status").GetString());
        Assert.Equal("image/jpeg", photo.GetProperty("contentType").GetString());
        // The test photo's EXIF says "rotate 90°", which is applied before the EXIF is dropped.
        Assert.Equal(30, photo.GetProperty("width").GetInt32());
        Assert.Equal(60, photo.GetProperty("height").GetInt32());
        Assert.Equal("[image]", await PreviewAsync(owner, conversation));
        Assert.Equal("human", await ResponderAsync(owner, conversation));

        var stored = await owner.Http.GetByteArrayAsync(photo.GetProperty("url").GetString());
        Assert.DoesNotContain("GPS-45.4642N", Encoding.ASCII.GetString(stored), StringComparison.Ordinal);

        await Api.SendRepliesAsync();

        var call = Api.Meta.Sends[^1];
        Assert.EndsWith($"/{pageId}/messages", call.Path, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(call.Body);
        Assert.Equal(psid, body.RootElement.GetProperty("recipient").GetProperty("id").GetString());
        Assert.Equal("image", body.RootElement.GetProperty("message").GetProperty("attachment").GetProperty("type").GetString());
        Assert.Equal("image/jpeg", body.RootElement.GetProperty("filedata").GetProperty("contentType").GetString());
        // Meta gets the re-encoded copy, the same one the team sees.
        Assert.Equal(stored, Api.Meta.LastUpload);

        var message = (await MessagesAsync(owner, conversation)).Last();
        Assert.Equal("sent", message.GetProperty("delivery").GetString());
    }

    [Fact]
    public async Task A_program_renamed_jpg_is_refused_and_nothing_is_kept()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var conversation = await ReceiveAsync(pageId, [], text: "Ciao");
        var filesBefore = FileCount();

        var response = await SendPhotoAsync(owner, conversation, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "media.type_not_allowed");
        Assert.Equal(filesBefore, FileCount());
        Assert.Single(await MessagesAsync(owner, conversation));
    }

    [Fact]
    public async Task A_photo_over_the_limit_is_refused()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var conversation = await ReceiveAsync(pageId, [], text: "Ciao");

        var response = await SendPhotoAsync(owner, conversation, new byte[(10 * 1024 * 1024) + 1]);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "media.too_large");
    }

    [Fact]
    public async Task No_photo_goes_out_after_the_24_hour_window()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        // The customer wrote 25 hours ago (moving the clock forward would end the sign-in instead).
        string conversation;
        Api.Clock.Advance(TimeSpan.FromHours(-25));
        try
        {
            conversation = await ReceiveAsync(pageId, [], text: "Ciao");
        }
        finally
        {
            Api.Clock.Advance(TimeSpan.FromHours(25));
        }

        var response = await SendPhotoAsync(owner, conversation, MediaImageTests.Png(10, 10));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "conversation.outside_window");
    }

    [Fact]
    public async Task Another_workspace_cannot_send_a_photo_into_the_conversation()
    {
        var (_, pageId) = await ConnectedOwnerAsync();
        var conversation = await ReceiveAsync(pageId, [], text: "Ciao");
        var (other, _) = await ConnectedOwnerAsync();

        var response = await SendPhotoAsync(other, conversation, MediaImageTests.Png(10, 10));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "conversation.not_found");
    }

    [Fact]
    public async Task Media_older_than_the_retention_period_is_removed_and_the_chat_says_so()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var recent = await ReceiveAsync(pageId, [("image", SavedPhoto("recent.png"))]);

        // Only this test's second photo is old: the other tests' media stays.
        var url = SavedPhoto("old.png");
        string conversation;
        Api.Clock.Advance(TimeSpan.FromDays(-366));
        try
        {
            conversation = await ReceiveAsync(pageId, [("image", url)]);
            await Api.DownloadMediaAsync();
        }
        finally
        {
            Api.Clock.Advance(TimeSpan.FromDays(366));
        }

        await Api.DownloadMediaAsync();
        var saved = (await AttachmentsAsync(owner, conversation)).Single();
        var filesBefore = FileCount();

        Assert.Equal(1, await Api.PurgeOldMediaAsync());
        Assert.Equal(0, await Api.PurgeOldMediaAsync());

        Assert.Equal("saved", (await AttachmentsAsync(owner, recent)).Single().GetProperty("status").GetString());
        var removed = (await AttachmentsAsync(owner, conversation)).Single();
        Assert.Equal("deleted", removed.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, removed.GetProperty("url").ValueKind);
        // Full copy, thumbnail and AI copy.
        Assert.Equal(filesBefore - 3, FileCount());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync(saved.GetProperty("url").GetString())).StatusCode);
    }

    [Fact]
    public async Task Erasing_a_contact_deletes_their_media_files_and_rows()
    {
        var (owner, pageId) = await ConnectedOwnerAsync();
        var url = Cdn("erase.png");
        Api.Web.Pages[url] = ("image/png", MediaImageTests.Png(20, 20));
        var conversation = await ReceiveAsync(pageId, [("image", url)]);
        await Api.DownloadMediaAsync();
        await SendPhotoAsync(owner, conversation, MediaImageTests.Png(12, 12));
        var filesBefore = FileCount();

        var detail = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversation}");
        var contactId = detail.GetProperty("contact").GetProperty("id").GetGuid();
        var me = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/me");
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(me.GetProperty("workspace").GetProperty("id").GetGuid());
            Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<IMediaErasure>().EraseContactAsync(contactId, CancellationToken.None));
        }

        Assert.Empty(await AttachmentsAsync(owner, conversation));
        // Customer photo: 3 files; team photo: copy and thumbnail.
        Assert.Equal(filesBefore - 5, FileCount());
    }

    private string SavedPhoto(string name)
    {
        var url = Cdn(name);
        Api.Web.Pages[url] = ("image/png", MediaImageTests.Png(30, 30));
        return url;
    }

    private static async Task<HttpResponseMessage> SendPhotoAsync(ApiClient client, string conversation, byte[] content)
    {
        await client.RefreshCsrfAsync();
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(content);
        // What the browser claims is never believed.
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "photo.jpg");
        return await client.Http.PostAsync($"/api/v1/conversations/{conversation}/images", form);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }
}
