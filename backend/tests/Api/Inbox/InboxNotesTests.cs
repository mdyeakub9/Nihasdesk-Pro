using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Inbox;

/// <summary>Team notes (PRD 4.4): pinned, @mentions, team-only (SEC-06, SEC-10, SEC-21, SEC-37).</summary>
[Collection(AuthTests.Name)]
public sealed partial class InboxNotesTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Notes_stay_in_time_order_when_pinned_and_pinned_ones_are_listed_for_the_composer()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n1", "Ciao")).ConversationId;

        var first = await AddNoteAsync(owner, id, "VIP, call after 6 pm");
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await AddNoteAsync(owner, id, "Asked about express shipping");
        Assert.True(first.GetProperty("internal").GetBoolean());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("pinnedAt").ValueKind);
        Assert.Equal("none", first.GetProperty("delivery").GetString());

        var pinned = await PinAsync(owner, id, first.GetProperty("id").GetString()!);
        Assert.Equal(HttpStatusCode.OK, pinned.StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await pinned.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("pinnedAt").ValueKind);

        var notes = await NotesAsync(owner, id);
        Assert.Equal(["VIP, call after 6 pm"], Texts(notes.GetProperty("pinned")));
        // Pinning doesn't move a note in the side panel's list.
        Assert.Equal(["Asked about express shipping", "VIP, call after 6 pm"], Texts(notes.GetProperty("items")));

        // Notes stay in the timeline too, and don't change the conversation.
        var timeline = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items");
        Assert.Equal(3, timeline.GetArrayLength());
        var conversation = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("Ciao", conversation.GetProperty("preview").GetString());
        Assert.Equal("ai", conversation.GetProperty("responder").GetString());

        await owner.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.OK, (await owner.Http.DeleteAsync($"/api/v1/conversations/{id}/notes/{first.GetProperty("id").GetString()}/pin")).StatusCode);
        notes = await NotesAsync(owner, id);
        Assert.Empty(notes.GetProperty("pinned").EnumerateArray());
        Assert.Equal(["Asked about express shipping", "VIP, call after 6 pm"], Texts(notes.GetProperty("items")));
    }

    [Fact]
    public async Task Resolving_a_note_dims_it_unpins_it_and_can_be_undone()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n7", "Ciao")).ConversationId;
        var noteId = (await AddNoteAsync(owner, id, "Call her back")).GetProperty("id").GetString()!;
        await PinAsync(owner, id, noteId);

        var resolved = await ResolveAsync(owner, id, noteId);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var body = await resolved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(await UserIdAsync(owner), body.GetProperty("resolvedByUserId").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("pinnedAt").ValueKind);

        var notes = await NotesAsync(owner, id);
        Assert.Empty(notes.GetProperty("pinned").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, Assert.Single(notes.GetProperty("items").EnumerateArray()).GetProperty("resolvedAt").ValueKind);

        var pinResolved = await PinAsync(owner, id, noteId);
        Assert.Equal(HttpStatusCode.Conflict, pinResolved.StatusCode);
        Assert.Equal("note.resolved", await AuthFlowTests.CodeOf(pinResolved));

        await owner.RefreshCsrfAsync();
        var reopened = await owner.Http.DeleteAsync($"/api/v1/conversations/{id}/notes/{noteId}/resolve");
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await reopened.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("resolvedAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await PinAsync(owner, id, noteId)).StatusCode);

        var customerMessage = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages"))
            .GetProperty("items")[0].GetProperty("id").GetString()!;
        await AssertNotFound(ResolveAsync(owner, id, customerMessage), "note.not_found");
    }

    [Fact]
    public async Task Only_notes_can_be_pinned_and_at_most_five()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n2", "Hello")).ConversationId;
        var customerMessage = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages"))
            .GetProperty("items")[0].GetProperty("id").GetString()!;

        var notNote = await PinAsync(owner, id, customerMessage);
        Assert.Equal(HttpStatusCode.NotFound, notNote.StatusCode);
        Assert.Equal("note.not_found", await AuthFlowTests.CodeOf(notNote));

        for (var i = 0; i < 5; i++)
        {
            var note = await AddNoteAsync(owner, id, $"Note {i}");
            Assert.Equal(HttpStatusCode.OK, (await PinAsync(owner, id, note.GetProperty("id").GetString()!)).StatusCode);
        }

        var sixth = await AddNoteAsync(owner, id, "One too many");
        var limit = await PinAsync(owner, id, sixth.GetProperty("id").GetString()!);
        Assert.Equal(HttpStatusCode.Conflict, limit.StatusCode);
        Assert.Equal("note.pin_limit", await AuthFlowTests.CodeOf(limit));
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
    public async Task Members_can_write_and_pin_notes()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var (member, _) = await MemberOfAsync(owner);
        var id = (await inbox.ReceiveAsync(workspace, "visitor-n4", "Ciao")).ConversationId;

        var note = await AddNoteAsync(member, id, "Member note");
        Assert.Equal("Luca", note.GetProperty("authorName").GetString());
        Assert.Equal(HttpStatusCode.OK, (await PinAsync(member, id, note.GetProperty("id").GetString()!)).StatusCode);
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
        await AssertNotFound(PinAsync(ownerB, id, note), "note.not_found");
        await AssertNotFound(PinAsync(ownerB, idB, note), "note.not_found");
        await ownerB.RefreshCsrfAsync();
        await AssertNotFound(ownerB.Http.DeleteAsync($"/api/v1/conversations/{id}/notes/{note}/pin"), "note.not_found");
        await AssertNotFound(ResolveAsync(ownerB, id, note), "note.not_found");
        await AssertNotFound(ResolveAsync(ownerB, idB, note), "note.not_found");
        await ownerB.RefreshCsrfAsync();
        await AssertNotFound(ownerB.Http.DeleteAsync($"/api/v1/conversations/{id}/notes/{note}/resolve"), "note.not_found");

        var notesB = await NotesAsync(ownerB, idB);
        Assert.Empty(notesB.GetProperty("items").EnumerateArray());
        var notesA = await NotesAsync(ownerA, id);
        var untouched = Assert.Single(notesA.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, untouched.GetProperty("pinnedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, untouched.GetProperty("resolvedAt").ValueKind);

        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.GetAsync($"/api/v1/conversations/{id}/notes")).StatusCode);
    }

    [Fact]
    public async Task Pinned_mentioned_and_resolved_notes_never_reach_the_ai()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var (_, memberId) = await MemberOfAsync(owner);
        var marker = "#" + Guid.NewGuid().ToString("N")[..8];
        Api.Ai.Respond = _ => FakeAiAnswer.Say("Certo!");

        var id = (await inbox.ReceiveAsync(workspace, "visitor-n6", $"Ciao {marker}")).ConversationId;
        var note = await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "SECRET-PINNED-NOTE call her", mentionedUserIds = new[] { memberId } });
        await PinAsync(owner, id, (await note.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!);
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
        Assert.DoesNotContain("SECRET-PINNED-NOTE", call.System, StringComparison.Ordinal);
        Assert.DoesNotContain(call.Messages, m => m.Text?.Contains("SECRET-PINNED-NOTE", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(call.Messages, m => m.Text?.Contains("SECRET-RESOLVED-NOTE", StringComparison.Ordinal) == true);
    }

    internal async Task<(ApiClient Client, string UserId)> MemberOfAsync(ApiClient owner)
    {
        var email = AuthFixture.NewEmail();
        await owner.PostAsync("/api/v1/team/invitations", new { email, role = "member" });
        var token = HttpUtility.ParseQueryString(new Uri(InvitationLink().Match(Api.Emails.LastTo(email).TextBody).Value).Query)["token"];
        var member = Api.CreateApiClient();
        (await member.PostAsync("/api/v1/invitations/accept", new { token, name = "Luca", password = ApiClient.DefaultPassword })).EnsureSuccessStatusCode();
        return (member, await UserIdAsync(member));
    }

    private static async Task<string> UserIdAsync(ApiClient client) =>
        (await client.Http.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("user").GetProperty("id").GetString()!;

    private static async Task<JsonElement> AddNoteAsync(ApiClient client, Guid id, string text)
    {
        var response = await client.PostAsync($"/api/v1/conversations/{id}/notes", new { text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> PinAsync(ApiClient client, Guid id, string noteId)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsync($"/api/v1/conversations/{id}/notes/{noteId}/pin", null);
    }

    private static async Task<HttpResponseMessage> ResolveAsync(ApiClient client, Guid id, string noteId)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsync($"/api/v1/conversations/{id}/notes/{noteId}/resolve", null);
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
