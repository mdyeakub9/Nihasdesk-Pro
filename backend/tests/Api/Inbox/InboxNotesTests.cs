using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>Team notes (PRD 4.4, ADR 0021): Notes section only, @mentions, resolve, delete, team-only (SEC-06, SEC-10, SEC-21, SEC-37).</summary>
[Collection(AuthTests.Name)]
public sealed partial class InboxNotesTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Notes_are_listed_newest_first_and_leave_the_conversation_unchanged()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n1", "Ciao")).ConversationId;

        var first = await AddNoteAsync(owner, id, "VIP, call after 6 pm");
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await AddNoteAsync(owner, id, "Asked about express shipping");
        Assert.True(first.GetProperty("internal").GetBoolean());
        Assert.Equal("none", first.GetProperty("delivery").GetString());
        Assert.False(first.TryGetProperty("pinnedAt", out _));

        var notes = await NotesAsync(owner, id);
        Assert.Equal(["Asked about express shipping", "VIP, call after 6 pm"], Texts(notes.GetProperty("items")));
        Assert.False(notes.TryGetProperty("pinned", out _));

        var conversation = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("Ciao", conversation.GetProperty("preview").GetString());
        Assert.Equal("ai", conversation.GetProperty("responder").GetString());
    }

    [Fact]
    public async Task Pinning_is_gone()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n2", "Hello")).ConversationId;
        var note = (await AddNoteAsync(owner, id, "Note")).GetProperty("id").GetString();

        await owner.RefreshCsrfAsync();
        var pin = await owner.Http.PutAsync($"/api/v1/conversations/{id}/notes/{note}/pin", null);
        Assert.True(pin.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"pin → {pin.StatusCode}");
    }

    [Fact]
    public async Task Ai_handoff_notes_stay_in_the_conversation_and_are_not_team_notes()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n8", "Voglio un rimborso")).ConversationId;
        await HandOffAsync(workspace, id, "The customer asked for a refund. Please answer this customer.");
        var aiNote = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages"))
            .GetProperty("items").EnumerateArray().Single(m => m.GetProperty("author").GetString() == "ai");
        Assert.True(aiNote.GetProperty("internal").GetBoolean());
        var aiNoteId = aiNote.GetProperty("id").GetString()!;

        Assert.Empty((await NotesAsync(owner, id)).GetProperty("items").EnumerateArray());
        await AssertNotFound(ResolveAsync(owner, id, aiNoteId), "note.not_found");
        await AssertNotFound(DeleteAsync(owner, id, aiNoteId), "note.not_found");
    }

    [Fact]
    public async Task Resolving_a_note_dims_it_and_can_be_undone()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n7", "Ciao")).ConversationId;
        var noteId = (await AddNoteAsync(owner, id, "Call her back")).GetProperty("id").GetString()!;

        var resolved = await ResolveAsync(owner, id, noteId);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var body = await resolved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(await UserIdAsync(owner), body.GetProperty("resolvedByUserId").GetString());
        Assert.NotEqual(JsonValueKind.Null, Assert.Single((await NotesAsync(owner, id)).GetProperty("items").EnumerateArray()).GetProperty("resolvedAt").ValueKind);

        await owner.RefreshCsrfAsync();
        var reopened = await owner.Http.DeleteAsync($"/api/v1/conversations/{id}/notes/{noteId}/resolve");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await reopened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resolvedAt").ValueKind);

        var customerMessage = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages"))
            .GetProperty("items")[0].GetProperty("id").GetString()!;
        await AssertNotFound(ResolveAsync(owner, id, customerMessage), "note.not_found");
    }

    [Fact]
    public async Task The_author_an_owner_or_an_admin_can_delete_a_note_and_other_members_cannot()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var (member, _) = await MemberOfAsync(owner);
        var (otherMember, _) = await MemberOfAsync(owner);
        var (admin, _) = await MemberOfAsync(owner, "admin");
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n9", "Ciao")).ConversationId;

        var byMember = (await AddNoteAsync(member, id, "Member's note")).GetProperty("id").GetString()!;
        var forbidden = await DeleteAsync(otherMember, id, byMember);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("note.not_author", await AuthFlowTests.CodeOf(forbidden));
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(member, id, byMember)).StatusCode);

        var forAdmin = (await AddNoteAsync(member, id, "Admin removes this")).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(admin, id, forAdmin)).StatusCode);
        var forOwner = (await AddNoteAsync(member, id, "Owner removes this")).GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(owner, id, forOwner)).StatusCode);

        Assert.Empty((await NotesAsync(owner, id)).GetProperty("items").EnumerateArray());
        await AssertNotFound(DeleteAsync(owner, id, forOwner), "note.not_found");
        var timeline = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items");
        Assert.DoesNotContain(timeline.EnumerateArray(), m => m.GetProperty("internal").GetBoolean());
    }

    [Fact]
    public async Task Mentions_accept_members_of_this_workspace_only()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync("Almanaam");
        var (ownerB, _) = await inbox.OwnerAsync("Bottega Blu");
        var (_, memberId) = await MemberOfAsync(owner);
        var strangerId = await UserIdAsync(ownerB);
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n3", "Ciao")).ConversationId;

        var note = await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "@Luca can you call her?", mentionedUserIds = new[] { memberId, memberId } });
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        Assert.Equal([memberId], (await note.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mentionedUserIds").EnumerateArray().Select(m => m.GetString()));

        var stranger = await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "@someone", mentionedUserIds = new[] { strangerId } });
        Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
        Assert.Equal("note.mention_not_member", await AuthFlowTests.CodeOf(stranger));

        var tooMany = Enumerable.Range(0, 11).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "x", mentionedUserIds = tooMany })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "x", mentionedUserIds = new[] { Guid.Empty } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = new string('x', 4001) })).StatusCode);
    }

    [Fact]
    public async Task Another_workspace_gets_not_found_for_notes()
    {
        var inbox = new InboxTests(fixture);
        var (ownerA, workspaceA) = await inbox.OwnerAsync("Almanaam");
        var (ownerB, workspaceB) = await inbox.OwnerAsync("Bottega Blu");
        var id = (await inbox.ReceiveAsync(workspaceA, "visitor-n5", "Private")).ConversationId;
        var note = (await AddNoteAsync(ownerA, id, "SECRET-TEAM-NOTE")).GetProperty("id").GetString()!;
        // B's own conversation: A's note ID must not be reachable through it either.
        var idB = (await inbox.ReceiveAsync(workspaceB, "visitor-n5", "Hi")).ConversationId;

        await AssertNotFound(ownerB.Http.GetAsync($"/api/v1/conversations/{id}/notes"), "conversation.not_found");
        await AssertNotFound(ownerB.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "hi" }), "conversation.not_found");
        await AssertNotFound(ResolveAsync(ownerB, id, note), "note.not_found");
        await AssertNotFound(ResolveAsync(ownerB, idB, note), "note.not_found");
        await ownerB.RefreshCsrfAsync();
        await AssertNotFound(ownerB.Http.DeleteAsync($"/api/v1/conversations/{id}/notes/{note}/resolve"), "note.not_found");
        await AssertNotFound(DeleteAsync(ownerB, id, note), "note.not_found");
        await AssertNotFound(DeleteAsync(ownerB, idB, note), "note.not_found");

        Assert.Empty((await NotesAsync(ownerB, idB)).GetProperty("items").EnumerateArray());
        var untouched = Assert.Single((await NotesAsync(ownerA, id)).GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, untouched.GetProperty("resolvedAt").ValueKind);

        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync($"/api/v1/conversations/{id}/notes")).StatusCode);
    }

    [Fact]
    public async Task Mentioned_and_resolved_notes_never_reach_the_ai()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var (_, memberId) = await MemberOfAsync(owner);
        var marker = "#" + Guid.NewGuid().ToString("N")[..8];
        Api.Ai.Respond = _ => FakeAiAnswer.Say("Certo!");

        var id = (await inbox.ReceiveAsync(workspace, "visitor-n6", $"Ciao {marker}")).ConversationId;
        await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "SECRET-MENTION-NOTE call her", mentionedUserIds = new[] { memberId } });
        var resolved = await AddNoteAsync(owner, id, "SECRET-RESOLVED-NOTE refund done");
        await ResolveAsync(owner, id, resolved.GetProperty("id").GetString()!);
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await inbox.ReceiveAsync(workspace, "visitor-n6", "e le taglie?");
        // Earlier tests leave other workspaces' conversations due too; run until this one was answered.
        bool Mine(FakeAiCall c) => c.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);
        for (var i = 0; i < 10 && !Api.Ai.Calls.Any(Mine); i++)
        {
            await Api.RunAiAsync();
        }

        var call = Assert.Single(Api.Ai.Calls, Mine);
        Assert.DoesNotContain("SECRET-", call.System, StringComparison.Ordinal);
        Assert.DoesNotContain(call.Messages, m => m.Text?.Contains("SECRET-", StringComparison.Ordinal) == true);
    }

    internal async Task<(ApiClient Client, string UserId)> MemberOfAsync(ApiClient owner, string role = "member")
    {
        var email = AuthFixture.NewEmail();
        await owner.PostAsync("/api/v1/team/invitations", new { email, role });
        var token = HttpUtility.ParseQueryString(new Uri(InvitationLink().Match(Api.Emails.LastTo(email).TextBody).Value).Query)["token"];
        var member = Api.CreateApiClient();
        (await member.PostAsync("/api/v1/invitations/accept", new { token, name = "Luca", password = ApiClient.DefaultPassword })).EnsureSuccessStatusCode();
        return (member, await UserIdAsync(member));
    }

    /// <summary>The AI hands the conversation to the team with a note, as the agent does.</summary>
    private async Task HandOffAsync(string workspace, Guid conversationId, string note)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        await scope.ServiceProvider.GetRequiredService<IAiConversations>().HandOffAsync(conversationId, note, CancellationToken.None);
    }

    private static async Task<string> UserIdAsync(ApiClient client) =>
        (await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("id").GetString()!;

    private static async Task<JsonElement> AddNoteAsync(ApiClient client, Guid id, string text)
    {
        var response = await client.PostAsync($"/api/v1/conversations/{id}/notes", new { text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> ResolveAsync(ApiClient client, Guid id, string noteId)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsync($"/api/v1/conversations/{id}/notes/{noteId}/resolve", null);
    }

    private static async Task<HttpResponseMessage> DeleteAsync(ApiClient client, Guid id, string noteId)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.DeleteAsync($"/api/v1/conversations/{id}/notes/{noteId}");
    }

    private static async Task<JsonElement> NotesAsync(ApiClient client, Guid id) =>
        await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/notes");

    private static List<string?> Texts(JsonElement notes) =>
        notes.EnumerateArray().Select(n => n.GetProperty("text").GetString()).ToList();

    private static async Task AssertNotFound(Task<HttpResponseMessage> call, string code)
    {
        var response = await call;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(code, await AuthFlowTests.CodeOf(response));
    }

    [GeneratedRegex(@"https://\S+accept-invitation\?token=\S+")]
    private static partial Regex InvitationLink();
}
