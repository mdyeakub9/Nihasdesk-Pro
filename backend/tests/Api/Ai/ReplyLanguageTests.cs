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

/// <summary>Replies in the customer's language, by code (Issue #63), and Product type in the catalog search.</summary>
[Collection(AuthTests.Name)]
public sealed class ReplyLanguageTests(AuthFixture fixture)
{
    private const string Mixed = "The backpack is available in black. Vuoi che te lo metta da parte?";

    private static readonly string[] Colour = ["Colore"];

    private NihasdeskApiFactory Api => fixture.Api;

    [Theory]
    [InlineData("Do you ship to the USA?", ReplyLanguage.English)]
    [InlineData("can i combine two orders", ReplyLanguage.English)]
    [InlineData("How should I clean my leather bag?", ReplyLanguage.English)]
    [InlineData("Thanks", ReplyLanguage.English)]
    [InlineData("Can I combine two orders into one shipment?", ReplyLanguage.English)]
    [InlineData("Can I return an item I bought on sale?", ReplyLanguage.English)]
    [InlineData("Che orari fate?", ReplyLanguage.Italian)]
    [InlineData("Il contrassegno costa qualcosa in più?", ReplyLanguage.Italian)]
    [InlineData("Quanto costa?", ReplyLanguage.Italian)]
    [InlineData("ok", null)]
    [InlineData("borsa nera", null)]
    [InlineData("delivery address change korbo", null)]
    [InlineData("দুটি অর্ডার একসাথে করা যাবে?", null)]
    [InlineData("", null)]
    public void Detects_the_language_only_when_it_is_clear(string text, string? expected) =>
        Assert.Equal(expected, ReplyLanguage.Detect(text));

    [Fact]
    public void A_message_with_only_an_address_keeps_the_language_of_the_one_before()
    {
        var at = DateTimeOffset.UnixEpoch;
        AiHistoryMessage[] history =
        [
            new("customer", "Vorrei una borsa nera per favore", at),
            new("customer", "I'd like two blue wallets, delivered to Milan.", at),
            new("ai", "Vuoi che proceda con l'ordine?", at),
            new("customer", "Marco Test, +39 333 000 0002, Via Esempio 10, 20121 Milano MI", at),
        ];

        Assert.Equal(ReplyLanguage.English, ReplyLanguage.OfCustomer(history));
    }

    [Theory]
    [InlineData(Mixed, ReplyLanguage.English, true)]
    [InlineData("We have the Borsa a tracolla in pelle in black. Do you want it?", ReplyLanguage.English, false)]
    [InlineData("It costs €89.00 and ships in 2-4 days.", ReplyLanguage.English, false)]
    [InlineData("Lo zaino è disponibile. Do you want me to keep it for you?", ReplyLanguage.Italian, true)]
    [InlineData("Lo zaino è disponibile. Vuoi che te lo metta da parte?", ReplyLanguage.Italian, false)]
    public void Notices_a_sentence_in_the_other_language(string reply, string language, bool expected) =>
        Assert.Equal(expected, ReplyLanguage.HasOtherLanguageSentence(reply, language));

    [Fact]
    public async Task An_english_customer_is_asked_for_in_english_and_a_mixed_reply_is_written_again_before_it_is_sent()
    {
        var (owner, workspace) = await OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = c => IsFix(c) ? FakeAiAnswer.Say("The backpack is available in black. Shall I keep it for you?")
            : Mine(c, marker) ? FakeAiAnswer.Say(Mixed)
            : FakeAiAnswer.Say("ok");

        var id = await ReceiveAsync(workspace, $"Is the backpack available? Can you tell me? {marker}");
        await Api.RunAiAsync();

        Assert.Contains("Write your whole reply in English", Api.Ai.Calls.Last(c => Mine(c, marker)).System, StringComparison.Ordinal);
        var fix = Api.Ai.Calls.Last(IsFix);
        Assert.Equal(Mixed, Assert.Single(fix.Messages).Text);
        Assert.Empty(fix.Tools);
        Assert.Equal("The backpack is available in black. Shall I keep it for you?", (await AiMessagesAsync(owner, id))[^1]);

        var run = await RunAsync(workspace, id);
        Assert.Contains(run.Trace!.Tools, t => t.Name == "fix_reply_language");
        // Both calls are metered.
        Assert.Equal(200, run.InputTokens);
    }

    [Fact]
    public async Task A_written_again_reply_still_goes_through_the_reply_check()
    {
        var (owner, workspace) = await OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = c => IsFix(c) ? FakeAiAnswer.Say("It costs only €5.00, shall I keep it?")
            : Mine(c, marker) ? FakeAiAnswer.Say(Mixed)
            : FakeAiAnswer.Say("ok");

        var id = await ReceiveAsync(workspace, $"Is the backpack available? Can you tell me? {marker}");
        await Api.RunAiAsync();

        Assert.DoesNotContain(await AiMessagesAsync(owner, id), m => m.Contains("€5.00", StringComparison.Ordinal));
        Assert.Equal("price_not_from_tool", (await RunAsync(workspace, id)).Detail);
    }

    [Fact]
    public async Task If_writing_it_again_fails_the_customer_still_gets_the_reply()
    {
        var (owner, workspace) = await OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = c => IsFix(c) ? throw new LlmException("http_503")
            : Mine(c, marker) ? FakeAiAnswer.Say(Mixed)
            : FakeAiAnswer.Say("ok");

        var id = await ReceiveAsync(workspace, $"Is the backpack available? Can you tell me? {marker}");
        await Api.RunAiAsync();

        Assert.Equal(Mixed, (await AiMessagesAsync(owner, id))[^1]);
    }

    [Fact]
    public async Task A_reply_in_the_right_language_is_not_written_again_and_unclear_messages_get_no_instruction()
    {
        var (_, workspace) = await OwnerAsync();
        var marker = Marker();
        var calls = Api.Ai.Calls.Count(IsFix);
        Api.Ai.Respond = c => IsFix(c) ? FakeAiAnswer.Say("no") : FakeAiAnswer.Say("Certo, lo zaino è disponibile. Vuoi che te lo metta da parte?");

        await ReceiveAsync(workspace, $"Quanto costa lo zaino? {marker}", from: "v-it");
        var unclear = Marker();
        await ReceiveAsync(workspace, $"korbo {unclear}", from: "v-other");
        await Api.RunAiAsync();

        Assert.Contains("Write your whole reply in Italian", Api.Ai.Calls.Last(c => Mine(c, marker)).System, StringComparison.Ordinal);
        Assert.DoesNotContain("Write your whole reply", Api.Ai.Calls.Last(c => Mine(c, unclear)).System, StringComparison.Ordinal);
        Assert.Equal(calls, Api.Ai.Calls.Count(IsFix));
    }

    [Fact]
    public async Task The_teams_assistant_is_not_checked_because_its_summary_is_in_the_teams_language()
    {
        var (owner, workspace) = await OwnerAsync();
        var marker = Marker();
        Api.Ai.Respond = c => FakeAiAnswer.Say("SUMMARY: Il cliente vuole sapere se lo zaino è disponibile.\nREPLY: Yes, it is available.");
        var id = await ReceiveAsync(workspace, $"Is the backpack available? Can you tell me? {marker}");
        await Api.RunAiAsync();
        var calls = Api.Ai.Calls.Count(IsFix);

        var assist = await owner.PostAsync($"/api/v1/conversations/{id}/assist", new { language = "it" });
        assist.EnsureSuccessStatusCode();

        Assert.Equal(calls, Api.Ai.Calls.Count(IsFix));
        Assert.Equal("Yes, it is available.", (await assist.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reply").GetString());
    }

    [Fact]
    public async Task The_catalog_search_finds_a_product_by_its_type()
    {
        var (owner, workspace) = await OwnerAsync();
        (await owner.PostAsync("/api/v1/products", new
        {
            title = "Redmi Note 13",
            status = "active",
            productType = "Mobile",
            optionNames = Colour,
            variants = new object[] { new { sku = $"R-{Guid.NewGuid():N}"[..12], option1 = "Nero", price = 10000 } },
        })).EnsureSuccessStatusCode();
        var marker = Marker();
        Api.Ai.Respond = c => !Mine(c, marker) ? FakeAiAnswer.Say("ok")
            : c.ToolResults.Count == 0 ? FakeAiAnswer.Call("search_products", """{"query":"mobile"}""")
            : FakeAiAnswer.Say("ok");

        await ReceiveAsync(workspace, $"Avete dei telefoni? {marker}");
        await Api.RunAiAsync();

        Assert.Contains("Redmi Note 13", Api.Ai.Calls.Last(c => Mine(c, marker)).ToolResults.Single(), StringComparison.Ordinal);
    }

    private async Task<Nihasdesk.Modules.AiAgent.Domain.AiRun> RunAsync(string workspace, Guid conversation)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>().Runs.AsNoTracking()
            .Where(r => r.ConversationId == conversation)
            .OrderByDescending(r => r.CreatedAt)
            .FirstAsync();
    }

    private static string Marker() => "#" + Guid.NewGuid().ToString("N")[..8];

    private static bool IsFix(FakeAiCall call) => call.System.Contains("Rewrite the shop's message", StringComparison.Ordinal);

    private static bool Mine(FakeAiCall call, string marker) =>
        call.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);

    private async Task<(ApiClient Client, string Workspace)> OwnerAsync()
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Almanaam")).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetString()!);
    }

    private async Task<Guid> ReceiveAsync(string workspace, string text, string from = "visitor")
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var result = await scope.ServiceProvider.GetRequiredService<IInboxIngest>()
            .ReceiveAsync(new InboundMessage("website", from, "Giulia Rossi", text, null, Api.Clock.GetUtcNow()), CancellationToken.None);
        return result.ConversationId;
    }

    private static async Task<List<string>> AiMessagesAsync(ApiClient owner, Guid id) =>
        (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/messages?limit=100")).GetProperty("items").EnumerateArray()
            .Where(m => m.GetProperty("author").GetString() == "ai" && !m.GetProperty("internal").GetBoolean())
            .Select(m => m.GetProperty("text").GetString()!)
            .ToList();
}
