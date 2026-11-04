using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>Adding, listing and deleting knowledge sources (PRD 4.7, SEC-04, SEC-05, SEC-06, SEC-45).</summary>
[Collection(AuthTests.Name)]
public sealed class SourcesTests(AuthFixture fixture)
{
    private const string Path = "/api/v1/ai/sources";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Owners_add_pdf_docx_and_text_files_by_content_and_members_only_see_them()
    {
        var (owner, _) = await OwnerAsync();
        var member = await MemberOfAsync(owner);

        var pdf = await UploadAsync(owner, Pdf("listino"), "../../secret/Listino prezzi.pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        var created = await pdf.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("pdf", created.GetProperty("kind").GetString());
        Assert.Equal("reading", created.GetProperty("status").GetString());
        // Shown only, and never a path.
        Assert.Equal("Listino prezzi.pdf", created.GetProperty("title").GetString());

        // The name doesn't count: a Word file called .pdf is a Word file.
        var docx = await (await UploadAsync(owner, Docx(), "manuale.pdf")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("docx", docx.GetProperty("kind").GetString());
        var txt = await (await UploadAsync(owner, Encoding.UTF8.GetBytes("Orari: Lun–Sab"), "note.txt")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("txt", txt.GetProperty("kind").GetString());

        var listed = await member.Http.GetFromJsonAsync<JsonElement>(Path);
        Assert.Equal(3, listed.GetArrayLength());
        Assert.DoesNotContain(listed.EnumerateArray(), s => s.TryGetProperty("storageKey", out _));

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(member, Pdf("altro"), "altro.pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync($"{Path}/web", new { url = "https://93.184.216.34/" })).StatusCode);
        var id = created.GetProperty("id").GetString();
        await member.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Http.DeleteAsync($"{Path}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Files_of_other_types_and_oversized_files_are_refused()
    {
        var (owner, _) = await OwnerAsync();

        Assert.Equal("source.unsupported_type", await AuthFlowTests.CodeOf(await UploadAsync(owner, [0x4D, 0x5A, 0x90, 0x00], "programma.pdf")));
        Assert.Equal("source.unsupported_type", await AuthFlowTests.CodeOf(await UploadAsync(owner, Encoding.UTF8.GetBytes("<html></html>"), "pagina.html")));
        Assert.Equal("source.unsupported_type", await AuthFlowTests.CodeOf(await UploadAsync(owner, [0x41, 0x00, 0x42], "binario.txt")));
        Assert.Equal("source.unsupported_type", await AuthFlowTests.CodeOf(await UploadAsync(owner, Zip("altro.xml"), "archivio.docx")));
        Assert.Equal("source.no_file", await AuthFlowTests.CodeOf(await UploadAsync(owner, [], "vuoto.pdf")));

        var tooLarge = new byte[KnowledgeSource.MaxFileBytes + 1];
        "%PDF-"u8.CopyTo(tooLarge);
        var huge = await UploadAsync(owner, tooLarge, "enorme.pdf");
        Assert.True(huge.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, huge.StatusCode.ToString());

        Assert.Empty((await owner.Http.GetFromJsonAsync<JsonElement>(Path)).EnumerateArray());
    }

    [Fact]
    public async Task The_same_file_or_page_is_noticed_within_a_workspace_only()
    {
        var (owner, _) = await OwnerAsync();
        var (other, _) = await OwnerAsync("Bottega Blu");
        var file = Pdf("catalogo");

        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(owner, file, "catalogo.pdf")).StatusCode);
        Assert.Equal("source.duplicate", await AuthFlowTests.CodeOf(await UploadAsync(owner, file, "copia.pdf")));
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(other, file, "catalogo.pdf")).StatusCode);

        var page = new { url = "https://93.184.216.34/spedizioni" };
        var added = await owner.PostAsync($"{Path}/web", page);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Equal("webpage", (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("kind").GetString());
        Assert.Equal("source.duplicate", await AuthFlowTests.CodeOf(await owner.PostAsync($"{Path}/web", page)));
    }

    [Theory]
    [InlineData("http://127.0.0.1/admin", "source.blocked_url")]
    [InlineData("http://localhost/admin", "source.blocked_url")]
    [InlineData("http://10.0.0.5/", "source.blocked_url")]
    [InlineData("http://192.168.1.1/", "source.blocked_url")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "source.blocked_url")]
    [InlineData("http://[::1]/", "source.blocked_url")]
    [InlineData("ftp://93.184.216.34/file", "source.invalid_url")]
    [InlineData("https://93.184.216.34:8443/", "source.invalid_url")]
    [InlineData("https://user:secret@93.184.216.34/", "source.invalid_url")]
    [InlineData("non-un-indirizzo", "source.invalid_url")]
    public async Task Private_and_malformed_addresses_are_refused(string url, string code)
    {
        var (owner, _) = await OwnerAsync();

        Assert.Equal(code, await AuthFlowTests.CodeOf(await owner.PostAsync($"{Path}/web", new { url })));
        Assert.Empty((await owner.Http.GetFromJsonAsync<JsonElement>(Path)).EnumerateArray());
    }

    [Fact]
    public async Task A_workspace_can_have_at_most_100_sources()
    {
        var (owner, workspace) = await OwnerAsync();
        var tenant = Guid.Parse(workspace);
        await using (var scope = Scope(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            db.Sources.AddRange(Enumerable.Range(0, KnowledgeSource.MaxPerWorkspace)
                .Select(i => KnowledgeSource.FromWebPage(tenant, $"https://93.184.216.34/{i}", Guid.NewGuid())));
            await db.SaveChangesAsync();
        }

        Assert.Equal("source.limit_reached", await AuthFlowTests.CodeOf(await UploadAsync(owner, Pdf("troppi"), "troppi.pdf")));
        Assert.Equal("source.limit_reached", await AuthFlowTests.CodeOf(await owner.PostAsync($"{Path}/web", new { url = "https://93.184.216.34/nuova" })));
    }

    [Fact]
    public async Task Deleting_a_source_removes_its_sections_and_file_and_other_workspaces_get_not_found()
    {
        var (owner, workspace) = await OwnerAsync();
        var (other, _) = await OwnerAsync("Bottega Blu");
        var tenant = Guid.Parse(workspace);
        var created = await (await UploadAsync(owner, Pdf("da-cancellare"), "cancella.pdf")).Content.ReadFromJsonAsync<JsonElement>();
        var id = Guid.Parse(created.GetProperty("id").GetString()!);

        string path;
        await using (var scope = Scope(tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            db.Sections.Add(KnowledgeSection.Create(tenant, id, 0, "Spedizioni", 1, SectionKind.Text, "Spediamo in tutta Italia.", 6, "it"));
            await db.SaveChangesAsync();
            var key = (await db.Sources.SingleAsync(s => s.Id == id)).StorageKey!;
            path = System.IO.Path.Combine(Api.FilesFolder, key);
        }

        Assert.True(File.Exists(path));
        Assert.Equal(1, (await owner.Http.GetFromJsonAsync<JsonElement>(Path))[0].GetProperty("sectionCount").GetInt32());

        await other.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Http.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.True(File.Exists(path));

        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.False(File.Exists(path));
        await using var after = Scope(tenant);
        Assert.False(await after.ServiceProvider.GetRequiredService<AiDbContext>().Sections.AnyAsync(s => s.SourceId == id));
    }

    private static byte[] Pdf(string marker) => Encoding.ASCII.GetBytes($"%PDF-1.7\n% {marker}\n%%EOF\n");

    private static byte[] Docx() => Zip("[Content_Types].xml", "word/document.xml");

    private static byte[] Zip(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                zip.CreateEntry(entry);
            }
        }

        return buffer.ToArray();
    }

    private AsyncServiceScope Scope(Guid tenant)
    {
        var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenant);
        return scope;
    }

    private static async Task<HttpResponseMessage> UploadAsync(ApiClient client, byte[] content, string fileName)
    {
        await client.RefreshCsrfAsync();
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return await client.Http.PostAsync($"{Path}/files", form);
    }

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
        var token = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query)["token"];
        var member = Api.CreateApiClient();
        (await member.PostAsync("/api/v1/invitations/accept", new { token, name = "Luca", password = "girasole tramonto barca verde" })).EnsureSuccessStatusCode();
        return member;
    }
}
