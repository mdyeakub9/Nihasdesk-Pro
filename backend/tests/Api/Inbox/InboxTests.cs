using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.TestSupport;
using Npgsql;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>Contacts and conversations through the API (ADR 0016, SEC-06, SEC-11, SEC-21).</summary>
[Collection(AuthTests.Name)]
public sealed class InboxTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task First_message_creates_contact_and_conversation_and_repeats_are_ignored()
    {
        var (owner, workspace) = await OwnerAsync();

        var first = await ReceiveAsync(workspace, "visitor-1", "Ciao, avete la taglia M?", externalId: "m-1", name: "Giulia Rossi");
        var repeat = await ReceiveAsync(workspace, "visitor-1", "Ciao, avete la taglia M?", externalId: "m-1", name: "Giulia Rossi");
        Assert.False(first.Duplicate);
        Assert.True(repeat.Duplicate);
        Assert.Equal(first.MessageId, repeat.MessageId);

        var list = await ListAsync(owner);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Giulia Rossi", item.GetProperty("contactName").GetString());
        Assert.Equal("website", item.GetProperty("channel").GetString());
        Assert.Equal("open", item.GetProperty("status").GetString());
        Assert.Equal("ai", item.GetProperty("responder").GetString());
        Assert.True(item.GetProperty("unread").GetBoolean());
        Assert.Equal("Ciao, avete la taglia M?", item.GetProperty("preview").GetString());

        var detail = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{first.ConversationId}");
        var contactId = detail.GetProperty("contact").GetProperty("id").GetString();
        var contact = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/contacts/{contactId}");
        Assert.Equal(["website"], contact.GetProperty("channels").EnumerateArray().Select(c => c.GetString()));

        var messages = await MessagesAsync(owner, first.ConversationId);
        Assert.Single(messages.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Team_reply_takes_over_and_the_next_customer_message_waits_for_the_team()
    {
        var (owner, workspace) = await OwnerAsync();
        var id = (await ReceiveAsync(workspace, "visitor-2", "Is this in stock?")).ConversationId;

        var reply = await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Yes, we have it." });
        Assert.Equal(HttpStatusCode.Created, reply.StatusCode);
        var sent = await reply.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("agent", sent.GetProperty("author").GetString());
        Assert.Equal("Test Owner", sent.GetProperty("authorName").GetString());

        var afterReply = await GetAsync(owner, id);
        Assert.Equal("human", afterReply.GetProperty("responder").GetString());
        Assert.Equal("Yes, we have it.", afterReply.GetProperty("preview").GetString());

        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await ReceiveAsync(workspace, "visitor-2", "Great, I'll take two.");
        Assert.Equal("waiting", (await GetAsync(owner, id)).GetProperty("status").GetString());

        await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "Perfect!" });
        Assert.Equal("open", (await GetAsync(owner, id)).GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.OK, (await PatchAsync(owner, id, new { responder = "ai" })).StatusCode);
        Assert.Equal("ai", (await GetAsync(owner, id)).GetProperty("responder").GetString());

        var history = (await MessagesAsync(owner, id)).GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("author").GetString()).ToList();
        Assert.Equal(["customer", "agent", "customer", "agent"], history);
    }

    [Fact]
    public async Task Notes_are_internal_and_do_not_move_the_conversation()
    {
        var (owner, workspace) = await OwnerAsync();
        var id = (await ReceiveAsync(workspace, "visitor-3", "Do you ship to Milan?")).ConversationId;

        var note = await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "VIP customer, offer express." });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        Assert.True((await note.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("internal").GetBoolean());

        var conversation = await GetAsync(owner, id);
        Assert.Equal("Do you ship to Milan?", conversation.GetProperty("preview").GetString());
        Assert.Equal("ai", conversation.GetProperty("responder").GetString());
    }

    [Fact]
    public async Task Unread_is_per_member_and_clears_when_read()
    {
        var (owner, workspace) = await OwnerAsync();
        var id = (await ReceiveAsync(workspace, "visitor-4", "Hello?")).ConversationId;

        Assert.Single((await ListAsync(owner, "?unread=true")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/conversations/{id}/read")).StatusCode);
        Assert.Empty((await ListAsync(owner, "?unread=true")).GetProperty("items").EnumerateArray());
        Assert.False((await GetAsync(owner, id)).GetProperty("unread").GetBoolean());

        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await ReceiveAsync(workspace, "visitor-4", "Anyone there?");
        Assert.True((await GetAsync(owner, id)).GetProperty("unread").GetBoolean());
    }

    [Fact]
    public async Task Conversations_page_by_last_activity_and_filter()
    {
        var (owner, workspace) = await OwnerAsync();
        var ids = new List<string>();
        foreach (var visitor in new[] { "a", "b", "c" })
        {
            Api.Clock.Advance(TimeSpan.FromSeconds(1));
            ids.Add((await ReceiveAsync(workspace, visitor, $"From {visitor}")).ConversationId.ToString());
        }

        var first = await ListAsync(owner, "?limit=2");
        Assert.Equal([ids[2], ids[1]], Ids(first));
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await ListAsync(owner, $"?limit=2&cursor={Uri.EscapeDataString(cursor!)}");
        Assert.Equal([ids[0]], Ids(second));
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);

        await PatchAsync(owner, Guid.Parse(ids[0]), new { status = "done", leadStatus = "hot" });
        Assert.Equal([ids[0]], Ids(await ListAsync(owner, "?status=done")));
        Assert.Equal([ids[2], ids[1]], Ids(await ListAsync(owner, "?status=open")));
        Assert.Empty(Ids(await ListAsync(owner, "?channel=whatsapp")));
    }

    [Fact]
    public async Task Assigning_works_for_members_of_this_workspace_only()
    {
        var (owner, workspace) = await OwnerAsync();
        var id = (await ReceiveAsync(workspace, "visitor-5", "Help please")).ConversationId;
        var me = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var myId = me.GetProperty("user").GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(owner, id, myId)).StatusCode);
        Assert.Equal([id.ToString()], Ids(await ListAsync(owner, "?assignee=me")));
        Assert.Empty(Ids(await ListAsync(owner, "?assignee=unassigned")));

        var stranger = await AssignAsync(owner, id, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
        Assert.Equal("conversation.assignee_not_member", await AuthFlowTests.CodeOf(stranger));

        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(owner, id, null)).StatusCode);
        Assert.Equal([id.ToString()], Ids(await ListAsync(owner, "?assignee=unassigned")));
    }

    [Fact]
    public async Task Another_workspace_gets_not_found_everywhere()
    {
        var (_, workspaceA) = await OwnerAsync("Almanaam");
        var (ownerB, _) = await OwnerAsync("Bottega Blu");
        var conversation = await ReceiveAsync(workspaceA, "visitor-6", "Private question");
        var id = conversation.ConversationId;
        var contactId = await ContactIdAsync(workspaceA, id);

        Assert.Empty(Ids(await ListAsync(ownerB)));
        await AssertNotFound(ownerB.Http.GetAsync($"/api/v1/conversations/{id}"), "conversation.not_found");
        await AssertNotFound(ownerB.Http.GetAsync($"/api/v1/conversations/{id}/messages"), "conversation.not_found");
        await AssertNotFound(ownerB.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "hi" }), "conversation.not_found");
        await AssertNotFound(ownerB.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "hi" }), "conversation.not_found");
        await AssertNotFound(ownerB.PostAsync($"/api/v1/conversations/{id}/read"), "conversation.not_found");
        await AssertNotFound(PatchAsync(ownerB, id, new { status = "done" }), "conversation.not_found");
        await AssertNotFound(AssignAsync(ownerB, id, null), "conversation.not_found");
        await AssertNotFound(ownerB.Http.GetAsync($"/api/v1/contacts/{contactId}"), "contact.not_found");
        await ownerB.RefreshCsrfAsync();
        await AssertNotFound(ownerB.Http.PutAsJsonAsync($"/api/v1/contacts/{contactId}", new { name = "x" }), "contact.not_found");

        Assert.Single((await MessagesAsync(_owners[workspaceA], id)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Row_level_security_hides_and_blocks_other_workspaces_rows()
    {
        var (_, workspaceA) = await OwnerAsync("Almanaam");
        var (_, workspaceB) = await OwnerAsync("Bottega Blu");
        var id = (await ReceiveAsync(workspaceA, "visitor-7", "Secret")).ConversationId;

        await using var connection = new NpgsqlConnection(fixture.Database.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = new NpgsqlCommand("SELECT set_config('app.tenant_id', @b, false)", connection))
        {
            setTenant.Parameters.AddWithValue("b", workspaceB);
            await setTenant.ExecuteNonQueryAsync();
        }

        foreach (var table in new[] { "inbox.conversations", "inbox.messages", "contacts.contacts", "contacts.contact_identities" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE tenant_id = @a", connection);
            count.Parameters.AddWithValue("a", Guid.Parse(workspaceA));
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }

        await using var insert = new NpgsqlCommand(
            """
            INSERT INTO inbox.messages (id, tenant_id, conversation_id, author, text, internal, sent_at, created_at, updated_at)
            VALUES (@id, @a, @conversation, 'agent', 'injected', false, now(), now(), now())
            """,
            connection);
        insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
        insert.Parameters.AddWithValue("a", Guid.Parse(workspaceA));
        insert.Parameters.AddWithValue("conversation", id);
        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Requests_are_validated_and_need_a_session()
    {
        var (owner, workspace) = await OwnerAsync();
        var id = (await ReceiveAsync(workspace, "visitor-8", "Hi")).ConversationId;

        foreach (var query in new[] { "?status=pending", "?status=Open", "?status=1", "?channel=sms", "?assignee=everyone", "?cursor=nope", "?limit=500" })
        {
            var response = await owner.Http.GetAsync($"/api/v1/conversations{query}");
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{query} → {response.StatusCode}");
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await PatchAsync(owner, id, new { status = "closed" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/conversations/{id}/messages", new { text = new string('x', 4001) })).StatusCode);

        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync("/api/v1/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync($"/api/v1/contacts/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Contacts_can_be_edited_and_channel_names_never_overwrite_the_team()
    {
        var (owner, workspace) = await OwnerAsync();
        var id = (await ReceiveAsync(workspace, "visitor-9", "Hi", name: "gx_visitor")).ConversationId;
        var contactId = await ContactIdAsync(workspace, id);

        await owner.RefreshCsrfAsync();
        var updated = await owner.Http.PutAsJsonAsync(
            $"/api/v1/contacts/{contactId}",
            new { name = "Giulia Bianchi", phone = "+39 333 123 4567", email = "giulia@example.com", language = "it", notes = "Prefers WhatsApp" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        await ReceiveAsync(workspace, "visitor-9", "Me again", name: "gx_visitor");
        var contact = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/contacts/{contactId}");
        Assert.Equal("Giulia Bianchi", contact.GetProperty("name").GetString());
        Assert.Equal("it", contact.GetProperty("language").GetString());

        await owner.RefreshCsrfAsync();
        var invalid = await owner.Http.PutAsJsonAsync($"/api/v1/contacts/{contactId}", new { email = "not-an-email", phone = "<script>" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    internal async Task<(ApiClient Client, string WorkspaceId)> OwnerAsync(string business = "Almanaam")
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), business)).Content.ReadFromJsonAsync<JsonElement>();
        var workspace = me.GetProperty("workspace").GetProperty("id").GetString()!;
        _owners[workspace] = client;
        return (client, workspace);
    }

    private readonly Dictionary<string, ApiClient> _owners = [];

    /// <summary>A customer message as a channel would deliver it, in that workspace's scope.</summary>
    internal async Task<InboundResult> ReceiveAsync(string workspace, string from, string text, string? externalId = null, string? name = null)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var ingest = scope.ServiceProvider.GetRequiredService<IInboxIngest>();
        return await ingest.ReceiveAsync(new InboundMessage("website", from, name, text, externalId, Api.Clock.GetUtcNow()), CancellationToken.None);
    }

    private async Task<string> ContactIdAsync(string workspace, Guid conversationId)
    {
        var owner = _owners[workspace];
        var detail = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversationId}");
        return detail.GetProperty("contact").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ListAsync(ApiClient client, string query = "") =>
        await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations{query}");

    private static async Task<JsonElement> GetAsync(ApiClient client, Guid id) =>
        (await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");

    private static async Task<JsonElement> MessagesAsync(ApiClient client, Guid id) =>
        await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages");

    private static async Task<HttpResponseMessage> PatchAsync(ApiClient client, Guid id, object body)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PatchAsJsonAsync($"/api/v1/conversations/{id}", body);
    }

    private static async Task<HttpResponseMessage> AssignAsync(ApiClient client, Guid id, string? userId)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsJsonAsync($"/api/v1/conversations/{id}/assignee", new { userId });
    }

    private static List<string> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    private static async Task AssertNotFound(Task<HttpResponseMessage> call, string code)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(code, await AuthFlowTests.CodeOf(response));
    }

}
