using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>Settings → AI and the test chat (PRD 4.8, ADR 0019).</summary>
[Collection(AuthTests.Name)]
public sealed class AiSettingsTests(AuthFixture fixture)
{
    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task Owners_change_tone_guidance_and_disclosure_and_the_ai_follows_them()
    {
        var (owner, workspace) = await OwnerAsync("Bottega Verde");
        var defaults = await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/ai/settings");
        Assert.True(defaults.GetProperty("enabled").GetBoolean());
        Assert.Equal("friendly", defaults.GetProperty("tone").GetString());
        Assert.Contains("Bottega Verde", defaults.GetProperty("defaultDisclosure").GetString(), StringComparison.Ordinal);
        Assert.True(defaults.GetProperty("providerConfigured").GetBoolean());

        var saved = await PutAsync(owner, new
        {
            enabled = true,
            tone = "formal",
            instructions = "Ricorda sempre che la spedizione è gratuita sopra i 50 euro.",
            disclosure = "Buongiorno, sono l'assistente digitale di Bottega Verde.",
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var marker = "#" + Guid.NewGuid().ToString("N")[..8];
        Api.Ai.Respond = c => FakeAiAnswer.Say(Mine(c, marker) ? "Certamente, come posso aiutarLa?" : "ok");
        var id = await ReceiveAsync(workspace, $"Buongiorno {marker}");
        await Api.RunAiAsync();

        var call = Api.Ai.Calls.Last(c => Mine(c, marker));
        Assert.Contains("spedizione è gratuita sopra i 50 euro", call.System, StringComparison.Ordinal);
        Assert.Contains("formal", call.System, StringComparison.Ordinal);
        var ai = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages")).GetProperty("items")
            .EnumerateArray().Where(m => m.GetProperty("author").GetString() == "ai" && !m.GetProperty("internal").GetBoolean())
            .Select(m => m.GetProperty("text").GetString()).ToList();
        Assert.Equal(["Buongiorno, sono l'assistente digitale di Bottega Verde.", "Certamente, come posso aiutarLa?"], ai);
    }

    [Fact]
    public async Task Switching_the_ai_off_hands_new_conversations_to_the_team()
    {
        var (owner, workspace) = await OwnerAsync();
        await PutAsync(owner, new { enabled = false, tone = "friendly" });
        var marker = "#" + Guid.NewGuid().ToString("N")[..8];
        Api.Ai.Respond = _ => FakeAiAnswer.Say("should not be called for this one");

        var id = await ReceiveAsync(workspace, $"Ciao {marker}");
        await Api.RunAiAsync();

        Assert.DoesNotContain(Api.Ai.Calls, c => Mine(c, marker));
        var conversation = (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}")).GetProperty("conversation");
        Assert.Equal("human", conversation.GetProperty("responder").GetString());
        Assert.Equal("waiting", conversation.GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_test_chat_answers_like_the_real_ai_and_stores_nothing()
    {
        var (owner, _) = await OwnerAsync("Almanaam");
        await owner.PostAsync("/api/v1/products", new
        {
            title = "Sciarpa di seta",
            status = "active",
            variants = new object[] { new { price = 3500 } },
        });
        var marker = "#" + Guid.NewGuid().ToString("N")[..8];
        Api.Ai.Respond = c => !Mine(c, marker) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("search_products", """{"query":"sciarpa"}""")
            : FakeAiAnswer.Say("Sì, la sciarpa di seta costa €35.00!");

        var response = await owner.PostAsync("/api/v1/ai/test", new { messages = new[] { new { author = "customer", text = $"Avete sciarpe? {marker}" } } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Sì, la sciarpa di seta costa €35.00!", result.GetProperty("reply").GetString());
        Assert.Contains("Almanaam", result.GetProperty("disclosure").GetString(), StringComparison.Ordinal);
        Assert.Equal(["search_products"], result.GetProperty("tools").EnumerateArray().Select(t => t.GetString()));
        Assert.Empty((await owner.Http.GetFromJsonAsync<JsonElement>("/api/v1/conversations")).GetProperty("items").EnumerateArray());

        var invented = "#" + Guid.NewGuid().ToString("N")[..8];
        Api.Ai.Respond = c => FakeAiAnswer.Say(Mine(c, invented) ? "Costa €9.99" : "ok");
        var held = await (await owner.PostAsync("/api/v1/ai/test", new
        {
            messages = new object[]
            {
                new { author = "customer", text = "Ciao" },
                new { author = "ai", text = "Ciao! Come posso aiutarti?" },
                new { author = "customer", text = $"Quanto costa? {invented}" },
            },
        })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("price_not_from_tool", held.GetProperty("blocked").GetString());
        Assert.Equal(JsonValueKind.Null, held.GetProperty("disclosure").ValueKind);
    }

    [Fact]
    public async Task Members_read_settings_but_only_owners_and_admins_change_or_test()
    {
        var (owner, _) = await OwnerAsync();
        var member = await MemberOfAsync(owner);

        Assert.Equal(HttpStatusCode.OK, (await member.Http.GetAsync("/api/v1/ai/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(member, new { enabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync("/api/v1/ai/test", new { messages = new[] { new { author = "customer", text = "hi" } } })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync(owner, new { enabled = true, tone = "rude" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/ai/test", new { messages = new[] { new { author = "ai", text = "hi" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/ai/test", new { messages = Array.Empty<object>() })).StatusCode);
    }

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

    private static async Task<HttpResponseMessage> PutAsync(ApiClient client, object body)
    {
        await client.RefreshCsrfAsync();
        return await client.Http.PutAsJsonAsync("/api/v1/ai/settings", body);
    }

    private async Task<Guid> ReceiveAsync(string workspace, string text)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var result = await scope.ServiceProvider.GetRequiredService<IInboxIngest>()
            .ReceiveAsync(new InboundMessage("website", "visitor", "Giulia Rossi", text, null, Api.Clock.GetUtcNow()), CancellationToken.None);
        return result.ConversationId;
    }
}
