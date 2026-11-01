using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;
using Npgsql;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>Saved replies (PRD 4.4, ADR 0022): Owner/Admin manage, everyone reads (SEC-06, SEC-10, SEC-11, SEC-21).</summary>
[Collection(AuthTests.Name)]
public sealed class SavedRepliesTests(AuthFixture fixture)
{
    private const string Path = "/api/v1/saved-replies";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Owner_creates_edits_and_deletes_and_the_list_is_sorted_by_shortcut()
    {
        var (owner, _) = await new InboxTests(fixture).OwnerAsync();

        var ship = await CreateAsync(owner, new { shortcut = "Ship", title = "Shipping times", textEn = "Hi {first_name}, we ship in 2 days.", textIt = "Ciao {first_name}, spediamo in 2 giorni." });
        Assert.Equal(HttpStatusCode.Created, ship.StatusCode);
        var created = await ship.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ship", created.GetProperty("shortcut").GetString());
        await CreateAsync(owner, new { shortcut = "address", title = "Our address", textIt = "Siamo in via Roma 1." });

        var list = await owner.Http.GetFromJsonAsync<JsonElement>(Path);
        Assert.Equal(["address", "ship"], list.EnumerateArray().Select(r => r.GetProperty("shortcut").GetString()));
        Assert.Equal(JsonValueKind.Null, list[0].GetProperty("textEn").ValueKind);

        var id = created.GetProperty("id").GetString();
        await owner.RefreshCsrfAsync();
        var updated = await owner.Http.PutAsJsonAsync($"{Path}/{id}", new { shortcut = "ship", title = "Shipping", textEn = "We ship in 1 day.", textIt = "" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("We ship in 1 day.", body.GetProperty("textEn").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("textIt").ValueKind);

        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Http.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.Single((await owner.Http.GetFromJsonAsync<JsonElement>(Path)).EnumerateArray());
    }

    [Fact]
    public async Task Members_read_but_cannot_change_and_admins_can()
    {
        var (owner, _) = await new InboxTests(fixture).OwnerAsync();
        var notes = new InboxNotesTests(fixture);
        var (member, _) = await notes.MemberOfAsync(owner);
        var (admin, _) = await notes.MemberOfAsync(owner, "admin");
        var id = (await (await CreateAsync(owner, new { shortcut = "hello", title = "Hello", textEn = "Hello!" })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetString();

        Assert.Single((await member.Http.GetFromJsonAsync<JsonElement>(Path)).EnumerateArray());
        await AssertForbidden(CreateAsync(member, new { shortcut = "nope", title = "No", textEn = "No" }));
        await member.RefreshCsrfAsync();
        await AssertForbidden(member.Http.PutAsJsonAsync($"{Path}/{id}", new { shortcut = "hello", title = "Changed", textEn = "x" }));
        await member.RefreshCsrfAsync();
        await AssertForbidden(member.Http.DeleteAsync($"{Path}/{id}"));

        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(admin, new { shortcut = "by-admin", title = "Admin's", textEn = "Hi" })).StatusCode);
        await admin.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Http.DeleteAsync($"{Path}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Requests_are_validated_and_shortcuts_are_unique_per_workspace()
    {
        var inbox = new InboxTests(fixture);
        var (owner, _) = await inbox.OwnerAsync("Almanaam");
        var (ownerB, _) = await inbox.OwnerAsync("Bottega Blu");

        foreach (var invalid in new object[]
        {
            new { shortcut = "", title = "T", textEn = "x" },
            new { shortcut = "has space", title = "T", textEn = "x" },
            new { shortcut = "/ship", title = "T", textEn = "x" },
            new { shortcut = new string('a', 31), title = "T", textEn = "x" },
            new { shortcut = "ok", title = "", textEn = "x" },
            new { shortcut = "ok", title = "T", textEn = "  ", textIt = "" },
            new { shortcut = "ok", title = "T", textEn = new string('x', 2001) },
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(owner, invalid)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(owner, new { shortcut = "ship", title = "A", textEn = "a" })).StatusCode);
        var duplicate = await CreateAsync(owner, new { shortcut = "SHIP", title = "B", textEn = "b" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("saved_reply.shortcut_taken", await AuthFlowTests.CodeOf(duplicate));
        // Another workspace may use the same shortcut.
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(ownerB, new { shortcut = "ship", title = "B", textEn = "b" })).StatusCode);
    }

    [Fact]
    public async Task Another_workspace_gets_not_found_and_the_database_hides_its_rows()
    {
        var inbox = new InboxTests(fixture);
        var (ownerA, workspaceA) = await inbox.OwnerAsync("Almanaam");
        var (ownerB, workspaceB) = await inbox.OwnerAsync("Bottega Blu");
        var id = (await (await CreateAsync(ownerA, new { shortcut = "secret", title = "A only", textEn = "SECRET-REPLY" })).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id").GetString();

        Assert.Empty((await ownerB.Http.GetFromJsonAsync<JsonElement>(Path)).EnumerateArray());
        await ownerB.RefreshCsrfAsync();
        var put = await ownerB.Http.PutAsJsonAsync($"{Path}/{id}", new { shortcut = "secret", title = "Taken over", textEn = "x" });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        Assert.Equal("saved_reply.not_found", await AuthFlowTests.CodeOf(put));
        await ownerB.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await ownerB.Http.DeleteAsync($"{Path}/{id}")).StatusCode);
        Assert.Equal("SECRET-REPLY", (await ownerA.Http.GetFromJsonAsync<JsonElement>(Path))[0].GetProperty("textEn").GetString());

        // Row-level security: as the app role with B's tenant, A's rows are invisible and can't be written.
        await using var connection = new NpgsqlConnection(fixture.Database.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @b, false)", connection))
        {
            setTenant.Parameters.AddWithValue("b", workspaceB);
            await setTenant.ExecuteNonQueryAsync();
        }

        await using (var count = new NpgsqlCommand("SELECT count(*) FROM inbox.saved_replies WHERE tenant_id = @a", connection))
        {
            count.Parameters.AddWithValue("a", Guid.Parse(workspaceA));
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }

        await using var insert = new NpgsqlCommand(
            "INSERT INTO inbox.saved_replies (id, tenant_id, shortcut, title, text_en, created_at, updated_at) VALUES (@id, @a, 'x', 'x', 'x', now(), now())",
            connection);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("a", Guid.Parse(workspaceA));
        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);

        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync(Path)).StatusCode);
    }

    private static Task<HttpResponseMessage> CreateAsync(ApiClient client, object body) => client.PostAsync(Path, body);

    private static async Task AssertForbidden(Task<HttpResponseMessage> call)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("team.forbidden", await AuthFlowTests.CodeOf(response));
    }
}
