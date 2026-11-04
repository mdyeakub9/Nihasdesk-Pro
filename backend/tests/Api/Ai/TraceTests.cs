using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>"Why this answer", 👍/👎 and the 👎 export (PRD 4.8, ADR 0026 §6, SEC-06, SEC-19, SEC-40).</summary>
[Collection(AuthTests.Name)]
public sealed class TraceTests(AuthFixture fixture)
{
    private static readonly string[] ColourOption = ["Colore"];

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Every_ai_reply_has_a_trace_with_tools_sources_and_the_reply_check()
    {
        var (owner, workspace) = await OwnerAsync();
        var conversation = await AnsweredAsync(owner, workspace);

        var replies = await RepliesAsync(owner, conversation);
        var answer = Assert.Single(replies);
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("rating").ValueKind);
        var messageId = answer.GetProperty("messageId").GetString()!;
        // The answer, not the disclosure before it, carries the trace.
        var ai = (await MessagesAsync(owner, conversation)).Where(m => m.GetProperty("author").GetString() == "ai").ToList();
        Assert.Equal(2, ai.Count);
        Assert.Equal(ai[1].GetProperty("id").GetString(), messageId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/v1/ai/messages/{ai[0].GetProperty("id").GetString()}/trace")).StatusCode);

        var trace = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/ai/messages/{messageId}/trace");
        Assert.Equal("replied", trace.GetProperty("outcome").GetString());
        Assert.StartsWith(Prompt.AgentVersion + "+", trace.GetProperty("instructionVersion").GetString(), StringComparison.Ordinal);
        Assert.Equal("passed", trace.GetProperty("check").GetString());
        Assert.True(trace.GetProperty("hasDetails").GetBoolean());
        var tool = Assert.Single(trace.GetProperty("tools").EnumerateArray());
        Assert.Equal("search_products", tool.GetProperty("name").GetString());
        Assert.Contains("borsa nera", tool.GetProperty("arguments").GetString(), StringComparison.Ordinal);
        Assert.Contains("89.00 EUR", tool.GetProperty("result").GetString(), StringComparison.Ordinal);
        // The cost price never reaches the model, so it is not in the trace either.
        Assert.DoesNotContain("4500", tool.GetProperty("result").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Held_back_drafts_record_which_check_failed()
    {
        var (owner, workspace) = await OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = c => FakeAiAnswer.Say(Mine(c, marker) ? "Costa solo €45.00!" : "ok");

        var conversation = await ReceiveAsync(workspace, $"Quanto costa? {marker}");
        await Api.RunAiAsync();

        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var run = await scope.ServiceProvider.GetRequiredService<AiDbContext>().Runs.SingleAsync(r => r.ConversationId == conversation);
        Assert.Null(run.MessageId);
        Assert.Equal("price_not_from_tool", run.Trace!.Check);
    }

    [Fact]
    public void The_customers_delivery_details_are_never_stored_in_a_trace()
    {
        var call = new LlmToolCall(
            "c1",
            "create_order_request",
            """{"items":[{"product_id":"p","variant_id":"v","quantity":1}],"zone_id":"z","customer_name":"Giulia Rossi","phone":"+39 333 1234567","street":"Via Roma 12","postal_code":"07100","city":"Sassari","country":"IT"}""");

        var trace = RunTraces.Of(call, new ToolOutcome("""{"ok":true,"orderNumber":1001}"""));

        Assert.DoesNotContain("Giulia", trace.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("333", trace.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("Via Roma", trace.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("07100", trace.Arguments, StringComparison.Ordinal);
        Assert.Contains("\"given\":[\"customer_name\",\"phone\",\"street\",\"postal_code\",\"city\",\"country\"]", trace.Arguments, StringComparison.Ordinal);
        Assert.Contains("zone_id", trace.Arguments, StringComparison.Ordinal);

        var longResult = RunTraces.Of(new LlmToolCall("c2", "search_products", "{}"), new ToolOutcome(new string('x', 2000)));
        Assert.Equal(RunTraces.MaxTextLength + 1, longResult.Result.Length);
    }

    [Fact]
    public async Task Members_rate_replies_each_on_their_own_and_another_workspace_sees_nothing()
    {
        var (owner, workspace) = await OwnerAsync();
        var member = await MemberOfAsync(owner);
        var conversation = await AnsweredAsync(owner, workspace);
        var messageId = Assert.Single(await RepliesAsync(owner, conversation)).GetProperty("messageId").GetString()!;
        var path = $"/api/v1/ai/messages/{messageId}/feedback";

        Assert.Equal("up", (await RateAsync(member, path, "up")).GetProperty("rating").GetString());
        Assert.Equal("down", (await RateAsync(member, path, "down")).GetProperty("rating").GetString());
        Assert.Equal("down", Assert.Single(await RepliesAsync(member, conversation)).GetProperty("rating").GetString());
        Assert.Equal(JsonValueKind.Null, Assert.Single(await RepliesAsync(owner, conversation)).GetProperty("rating").ValueKind);
        Assert.Equal("down", (await member.Http.GetFromJsonAsync<JsonElement>($"/api/v1/ai/messages/{messageId}/trace")).GetProperty("myRating").GetString());

        Assert.Equal(JsonValueKind.Null, (await RateAsync(owner, path, null)).GetProperty("rating").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(member, path, new { rating = "meh" })).StatusCode);

        // SEC-06: another workspace's reply is simply not found.
        var (stranger, _) = await OwnerAsync("Bottega Blu");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Http.GetAsync($"/api/v1/ai/messages/{messageId}/trace")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync(stranger, path, new { rating = "down" })).StatusCode);
        Assert.Empty(await RepliesAsync(stranger, conversation));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Http.GetAsync($"/api/v1/ai/messages/{Guid.NewGuid()}/trace")).StatusCode);
    }

    [Fact]
    public async Task Owners_export_disliked_replies_as_eval_cases_and_members_cannot()
    {
        var (owner, workspace) = await OwnerAsync();
        var member = await MemberOfAsync(owner);
        var conversation = await AnsweredAsync(owner, workspace);
        var messageId = Assert.Single(await RepliesAsync(owner, conversation)).GetProperty("messageId").GetString()!;
        await RateAsync(member, $"/api/v1/ai/messages/{messageId}/feedback", "down");

        var export = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/feedback/export");

        Assert.Contains("Remove names", export.GetProperty("notice").GetString(), StringComparison.Ordinal);
        var item = Assert.Single(export.GetProperty("cases").EnumerateArray());
        Assert.StartsWith("Avete la borsa nera?", item.GetProperty("question").GetString(), StringComparison.Ordinal);
        Assert.Equal("Sì, la borsa nera c'è! Costa €89.00 🙂", item.GetProperty("aiReply").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("expectHandoff").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.Http.GetAsync("/api/v1/ai/feedback/export")).StatusCode);
    }

    [Fact]
    public async Task The_test_chat_returns_the_same_trace()
    {
        var (owner, _) = await OwnerAsync();
        await CreateBagAsync(owner);
        var marker = Marker();
        Api.Ai.Respond = c => !Mine(c, marker) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("search_products", """{"query":"borsa"}""")
            : FakeAiAnswer.Say("Sì, costa €89.00!");

        var result = await (await owner.PostAsync("/api/v1/ai/test", new { messages = new[] { new { author = "customer", text = $"Avete borse? {marker}" } } }))
            .Content.ReadFromJsonAsync<JsonElement>();

        var trace = result.GetProperty("trace");
        Assert.Equal("passed", trace.GetProperty("check").GetString());
        Assert.Equal("search_products", Assert.Single(trace.GetProperty("tools").EnumerateArray()).GetProperty("name").GetString());
        Assert.StartsWith(Prompt.AgentVersion, trace.GetProperty("instructionVersion").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Traces_are_removed_after_the_retention_period_and_the_run_stays()
    {
        var (owner, workspace) = await OwnerAsync();
        var conversation = await AnsweredAsync(owner, workspace);
        var messageId = Assert.Single(await RepliesAsync(owner, conversation)).GetProperty("messageId").GetString()!;

        // Within the retention period nothing of this run is removed.
        await Api.Services.GetRequiredService<TraceRetention>().PurgeOnceAsync(CancellationToken.None);
        Assert.True((await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/ai/messages/{messageId}/trace")).GetProperty("hasDetails").GetBoolean());

        // As if the retention period were over: the app role may only run the narrow function.
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            var cutoff = Api.Clock.GetUtcNow().AddYears(1);
            Assert.True(await db.Database.SqlQuery<int>($"SELECT ai.purge_old_traces({cutoff}) AS \"Value\"").SingleAsync() > 0);
        }

        var trace = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/ai/messages/{messageId}/trace");
        Assert.False(trace.GetProperty("hasDetails").GetBoolean());
        Assert.Empty(trace.GetProperty("tools").EnumerateArray());
        Assert.Equal(["search_products"], trace.GetProperty("toolNames").EnumerateArray().Select(t => t.GetString()));
    }

    /// <summary>A customer asks for the bag and the AI answers from the catalog.</summary>
    private async Task<Guid> AnsweredAsync(ApiClient owner, string workspace)
    {
        await CreateBagAsync(owner);
        var marker = Marker();
        Api.Ai.Respond = call => !Mine(call, marker) ? FakeAiAnswer.Say("ok")
            : call.ToolResults.Count == 0 ? FakeAiAnswer.Call("search_products", """{"query":"borsa nera"}""")
            : FakeAiAnswer.Say("Sì, la borsa nera c'è! Costa €89.00 🙂");

        var conversation = await ReceiveAsync(workspace, $"Avete la borsa nera? {marker}");
        await Api.RunAiAsync();
        return conversation;
    }

    private static string Marker() => "#" + Guid.NewGuid().ToString("N")[..8];

    private static bool Mine(FakeAiCall call, string marker) =>
        call.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);

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

    private static async Task CreateBagAsync(ApiClient owner) =>
        (await owner.PostAsync("/api/v1/products", new
        {
            title = "Borsa nera in pelle",
            status = "active",
            optionNames = ColourOption,
            variants = new object[] { new { sku = $"B-{Guid.NewGuid():N}"[..12], option1 = "Nero", price = 8900, cost = 4500 } },
        })).EnsureSuccessStatusCode();

    private async Task<Guid> ReceiveAsync(string workspace, string text)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var result = await scope.ServiceProvider.GetRequiredService<IInboxIngest>()
            .ReceiveAsync(new InboundMessage("website", "visitor-" + Guid.NewGuid().ToString("N")[..6], "Giulia Rossi", text, null, Api.Clock.GetUtcNow()), CancellationToken.None);
        return result.ConversationId;
    }

    private static async Task<List<JsonElement>> MessagesAsync(ApiClient client, Guid id) =>
        (await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages?limit=100")).GetProperty("items").EnumerateArray().ToList();

    private static async Task<List<JsonElement>> RepliesAsync(ApiClient client, Guid conversation) =>
        (await client.Http.GetFromJsonAsync<JsonElement>($"/api/v1/ai/conversations/{conversation}/replies")).EnumerateArray().ToList();

    private static async Task<JsonElement> RateAsync(ApiClient client, string path, string? rating)
    {
        var response = await PutAsync(client, path, new { rating });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<HttpResponseMessage> PutAsync(ApiClient client, string path, object body)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsJsonAsync(path, body);
    }
}
