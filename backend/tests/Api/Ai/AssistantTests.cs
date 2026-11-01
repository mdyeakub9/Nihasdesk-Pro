using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Api.Tests.Inbox;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>The team's AI assistant (ADR 0023): drafts only, same guardrails, no notes (SEC-35–39, CLAUDE.md rules 5 and 7).</summary>
[Collection(AuthTests.Name)]
public sealed class AssistantTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Suggests_a_reply_from_the_catalog_with_a_summary_and_sources_and_sends_nothing()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync("Bottega Verde");
        await CreateBagAsync(owner);
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok")
            : call.ToolResults.Count == 0 ? FakeAiAnswer.Call("search_products", """{"query":"borsa nera"}""")
            : FakeAiAnswer.Say("SUMMARY: Giulia asks about the black bag.\nREPLY: Sì Giulia, la borsa nera c'è a €89.00!");

        var id = (await inbox.ReceiveAsync(workspace, "visitor-as1", $"Avete la borsa nera? {marker}", name: "Giulia Rossi")).ConversationId;
        var before = await MessagesAsync(owner, id);

        var response = await AssistAsync(owner, id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Giulia asks about the black bag.", body.GetProperty("summary").GetString());
        Assert.Equal("Sì Giulia, la borsa nera c'è a €89.00!", body.GetProperty("reply").GetString());
        Assert.Equal("catalog", body.GetProperty("sources")[0].GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("blocked").ValueKind);

        var call = Api.Ai.Calls.Last(c => Mine(c, marker));
        Assert.Contains("help the team", call.System, StringComparison.Ordinal);
        Assert.Contains("Giulia", call.System, StringComparison.Ordinal);
        Assert.DoesNotContain("Rossi", call.System, StringComparison.Ordinal);
        Assert.DoesNotContain("handoff_to_human", call.Tools);

        // A draft only: nothing new in the conversation, the AI stays the responder.
        Assert.Equal(before.Count, (await MessagesAsync(owner, id)).Count);
        Assert.Equal("ai", (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation").GetProperty("responder").GetString());
    }

    [Fact]
    public async Task A_price_the_tools_did_not_return_is_held_back()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = call => FakeAiAnswer.Say(Mine(call, marker) ? "SUMMARY: Price question.\nREPLY: Costa solo €19.99!" : "ok");

        var id = (await inbox.ReceiveAsync(workspace, "visitor-as2", $"Quanto costa? {marker}")).ConversationId;
        var body = await (await AssistAsync(owner, id)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("reply").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("summary").ValueKind);
        Assert.Equal("price_not_from_tool", body.GetProperty("blocked").GetString());
        Assert.DoesNotContain("19.99", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Team_notes_never_reach_the_ai_and_a_handoff_request_changes_nothing()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok")
            : call.ToolResults.Count == 0 ? FakeAiAnswer.Call("handoff_to_human", """{"reason":"refund"}""")
            : FakeAiAnswer.Say("SUMMARY: Refund request.\nREPLY: Ti aiutiamo subito.");

        var id = (await inbox.ReceiveAsync(workspace, "visitor-as3", $"Voglio un rimborso {marker}")).ConversationId;
        await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "SECRET-NOTE do not tell" });
        var resolved = await (await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = "SECRET-RESOLVED old" })).Content.ReadFromJsonAsync<JsonElement>();
        await owner.RefreshCsrfAsync();
        await owner.Http.PutAsync($"/api/v1/conversations/{id}/notes/{resolved.GetProperty("id").GetString()}/resolve", null);
        var before = await MessagesAsync(owner, id);

        var body = await (await AssistAsync(owner, id)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ti aiutiamo subito.", body.GetProperty("reply").GetString());

        foreach (var call in Api.Ai.Calls.Where(c => Mine(c, marker)))
        {
            Assert.DoesNotContain("SECRET-", call.System, StringComparison.Ordinal);
            Assert.DoesNotContain(call.Messages, m => m.Text?.Contains("SECRET-", StringComparison.Ordinal) == true);
        }

        // The model asked to hand over: refused as an unknown tool; no AI note, no status change.
        Assert.Contains("Unknown tool", Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults[0], StringComparison.Ordinal);
        Assert.Equal(before.Count, (await MessagesAsync(owner, id)).Count);
        var conversation = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("ai", conversation.GetProperty("responder").GetString());
        Assert.Equal("open", conversation.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Shorter_and_friendlier_only_change_the_instructions_and_every_member_may_ask()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        var (member, _) = await new InboxNotesTests(fixture).MemberOfAsync(owner);
        var marker = Marker();
        Api.Ai.Respond = call => FakeAiAnswer.Say(Mine(call, marker) ? "REPLY: Certo!" : "ok");
        var id = (await inbox.ReceiveAsync(workspace, "visitor-as4", $"Ciao {marker}")).ConversationId;

        Assert.Equal(HttpStatusCode.OK, (await AssistAsync(member, id, new { style = "shorter", language = "it" })).StatusCode);
        var shorter = Api.Ai.Calls.Last(c => Mine(c, marker)).System;
        Assert.Contains("as short as possible", shorter, StringComparison.Ordinal);
        Assert.Contains("in Italian", shorter, StringComparison.Ordinal);

        var friendlier = await (await AssistAsync(owner, id, new { style = "friendlier" })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("extra warm", Api.Ai.Calls.Last(c => Mine(c, marker)).System, StringComparison.Ordinal);
        // No markers: the whole text is the reply, without a summary.
        Assert.Equal("Certo!", friendlier.GetProperty("reply").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await AssistAsync(owner, id, new { style = "rude" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssistAsync(owner, id, new { language = "de" })).StatusCode);
    }

    [Fact]
    public async Task Suggestions_are_metered_limited_per_day_and_off_when_the_ai_is_off()
    {
        var inbox = new InboxTests(fixture);
        var (owner, workspace) = await inbox.OwnerAsync();
        Api.Ai.Respond = _ => FakeAiAnswer.Say("REPLY: Ok!");
        var id = (await inbox.ReceiveAsync(workspace, "visitor-as5", "Ciao")).ConversationId;

        // The test server allows 4 per workspace and day.
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await AssistAsync(owner, id)).StatusCode);
        }

        var limited = await AssistAsync(owner, id);
        Assert.Equal(HttpStatusCode.Conflict, limited.StatusCode);
        Assert.Equal("ai.suggest_limit", await AuthFlowTests.CodeOf(limited));

        var (other, otherWorkspace) = await inbox.OwnerAsync("Bottega Spenta");
        var otherId = (await inbox.ReceiveAsync(otherWorkspace, "visitor-as6", "Ciao")).ConversationId;
        await other.RefreshCsrfAsync();
        (await other.Http.PutAsJsonAsync("/api/v1/ai/settings", new { enabled = false, tone = "friendly" })).EnsureSuccessStatusCode();
        var off = await AssistAsync(other, otherId);
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        Assert.Equal("ai.off", await AuthFlowTests.CodeOf(off));
    }

    [Fact]
    public async Task Another_workspace_gets_not_found_and_the_ai_is_never_called()
    {
        var inbox = new InboxTests(fixture);
        var (_, workspaceA) = await inbox.OwnerAsync("Almanaam");
        var (ownerB, _) = await inbox.OwnerAsync("Bottega Blu");
        var marker = Marker();
        var id = (await inbox.ReceiveAsync(workspaceA, "visitor-as7", $"Private {marker}")).ConversationId;

        var response = await AssistAsync(ownerB, id);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("conversation.not_found", await AuthFlowTests.CodeOf(response));
        Assert.DoesNotContain(Api.Ai.Calls, c => Mine(c, marker));

        using var anonymous = Api.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Http.PostAsJsonAsync($"/api/v1/conversations/{id}/assist", new { })).StatusCode);
    }

    private static readonly string[] ColourOption = ["Colore"];

    private static Task<HttpResponseMessage> AssistAsync(ApiClient client, Guid id, object? body = null) =>
        client.PostAsync($"/api/v1/conversations/{id}/assist", body ?? new { });

    private static string Marker() => "#" + Guid.NewGuid().ToString("N")[..8];

    private static bool Mine(FakeAiCall call, string marker) =>
        call.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);

    private static async Task CreateBagAsync(ApiClient owner)
    {
        var created = await owner.PostAsync("/api/v1/products", new
        {
            title = "Borsa nera in pelle",
            status = "active",
            optionNames = ColourOption,
            variants = new object[] { new { sku = $"B-{Guid.NewGuid():N}"[..12], option1 = "Nero", price = 8900, cost = 4500 } },
        });
        created.EnsureSuccessStatusCode();
    }

    private static async Task<List<JsonElement>> MessagesAsync(ApiClient owner, Guid id) =>
        (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages?limit=100")).GetProperty("items").EnumerateArray().ToList();
}
