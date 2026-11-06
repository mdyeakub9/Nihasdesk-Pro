using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Api.Tests.Auth;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Contracts;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Llm;
using Nihasdesk.Modules.Inbox.Contracts;
using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Ai;

/// <summary>What the AI remembers of a long conversation (PRD 4.8, ADR 0026 §4, SEC-37, SEC-45).</summary>
[Collection(AuthTests.Name)]
public sealed class ConversationMemoryTests(AuthFixture fixture)
{
    private const string Filler = "Una domanda ancora sulla consegna e sul colore, per capire bene prima di decidere con calma cosa fare oggi. ";

    private NihasdeskApiFactory Api => fixture.Api;

    [Fact]
    public async Task A_60_message_conversation_keeps_the_chosen_product_and_size_in_the_context()
    {
        var (owner, workspace) = await OwnerAsync();
        var from = Marker();
        var id = await ReceiveAsync(workspace, from, "Ho scelto la borsa nera, taglia M");
        for (var i = 0; i < 58; i++)
        {
            await ReceiveAsync(workspace, from, $"{i} {Filler}{Filler}{Filler}");
        }

        var first = Marker();
        Api.Ai.Respond = c => FakeAiAnswer.Say(IsSummary(c) ? "Vuole la borsa nera, taglia M." : "Certo!");
        await ReceiveAsync(workspace, from, $"Allora? {first}");
        await Api.RunAiAsync();

        // The oldest messages no longer fit: the model gets the newest, and the job is asked to summarize.
        var call = Api.Ai.Calls.Last(c => Mine(c, first));
        Assert.DoesNotContain(call.Messages, m => m.Text?.Contains("taglia M", StringComparison.Ordinal) == true);
        Assert.True(call.Messages.Count < 59, $"{call.Messages.Count} messages went to the model.");

        await Api.RunSummariesAsync();
        Assert.Contains(Api.Ai.Calls, c => IsSummary(c) && c.Messages.Any(m => m.Text?.Contains("taglia M", StringComparison.Ordinal) == true));

        var second = Marker();
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        await ReceiveAsync(workspace, from, $"Quale avevo scelto? {second}");
        await Api.RunAiAsync();
        Assert.Contains("Vuole la borsa nera, taglia M.", Api.Ai.Calls.Last(c => Mine(c, second)).System, StringComparison.Ordinal);

        // The team reads it in the AI assistant tab.
        var memory = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/memory");
        Assert.Equal("Vuole la borsa nera, taglia M.", memory.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Team_notes_reach_neither_the_summary_nor_the_prompt()
    {
        var (owner, workspace) = await OwnerAsync();
        var from = Marker();
        var note = Marker();
        var id = await ReceiveAsync(workspace, from, "Buongiorno");
        (await owner.PostAsync($"/api/v1/conversations/{id}/notes", new { text = $"Cliente difficile {note}" })).EnsureSuccessStatusCode();
        for (var i = 0; i < 40; i++)
        {
            await ReceiveAsync(workspace, from, $"{i} {Filler}{Filler}{Filler}");
        }

        var last = Marker();
        Api.Ai.Respond = c => FakeAiAnswer.Say(IsSummary(c) ? "Chiede della consegna." : "Certo!");
        await ReceiveAsync(workspace, from, last);
        await Api.RunAiAsync();
        await Api.RunSummariesAsync();
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        var next = Marker();
        await ReceiveAsync(workspace, from, next);
        await Api.RunAiAsync();

        Assert.Contains(Api.Ai.Calls, IsSummary);
        Assert.Contains(Api.Ai.Calls, c => Mine(c, next));
        Assert.DoesNotContain(Api.Ai.Calls, c =>
            c.System.Contains(note, StringComparison.Ordinal) || Mine(c, note));
    }

    [Fact]
    public async Task A_short_conversation_needs_no_summary()
    {
        var (owner, workspace) = await OwnerAsync();
        var from = Marker();
        Api.Ai.Respond = _ => FakeAiAnswer.Say("Ciao!");
        var id = await ReceiveAsync(workspace, from, "Ciao");
        await Api.RunAiAsync();
        await Api.RunSummariesAsync();

        var memory = await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{id}/memory");
        Assert.Equal(JsonValueKind.Null, memory.GetProperty("summary").ValueKind);
    }

    [Fact]
    public async Task A_failing_model_keeps_the_context_and_is_tried_again_then_left_until_asked_again()
    {
        var (_, workspace) = await OwnerAsync();
        var (conversation, tenant) = await MemoryDueAsync(workspace);

        Api.Ai.Fail = true;
        try
        {
            for (var attempt = 1; attempt <= ConversationSummarizer.MaxAttempts; attempt++)
            {
                await Api.RunSummariesAsync();
                Assert.Equal(attempt, (await MemoryAsync(tenant, conversation)).SummaryAttempts);
                // Locked until the next try.
                await Api.RunSummariesAsync();
                Assert.Equal(attempt, (await MemoryAsync(tenant, conversation)).SummaryAttempts);
                Api.Clock.Advance(TimeSpan.FromMinutes(6));
            }
        }
        finally
        {
            Api.Ai.Fail = false;
        }

        var memory = await MemoryAsync(tenant, conversation);
        Assert.Null(memory.Summary);
        Assert.Null(memory.SummaryDueAt);
    }

    [Fact]
    public async Task Summaries_are_erased_with_the_contact_and_with_the_workspace()
    {
        var (_, workspace) = await OwnerAsync();
        var (conversation, tenant) = await MemoryDueAsync(workspace);
        var contact = (await MemoryAsync(tenant, conversation)).ContactId;

        await using (var scope = TenantScope(tenant))
        {
            await scope.ServiceProvider.GetRequiredService<IAiCustomerData>().EraseContactAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.True(await scope.ServiceProvider.GetRequiredService<AiDbContext>().Memory.AnyAsync(m => m.ConversationId == conversation));

            await scope.ServiceProvider.GetRequiredService<IAiCustomerData>().EraseContactAsync(contact, CancellationToken.None);
            Assert.False(await scope.ServiceProvider.GetRequiredService<AiDbContext>().Memory.AnyAsync(m => m.ConversationId == conversation));
        }

        var (other, _) = await MemoryDueAsync(workspace);
        await using (var scope = TenantScope(tenant))
        {
            await scope.ServiceProvider.GetRequiredService<IAiCustomerData>().EraseWorkspaceAsync(CancellationToken.None);
            Assert.False(await scope.ServiceProvider.GetRequiredService<AiDbContext>().Memory.AnyAsync(m => m.ConversationId == other));
        }
    }

    [Fact]
    public async Task Another_workspaces_summary_is_not_shown()
    {
        var (owner, workspace) = await OwnerAsync();
        var (conversation, _) = await MemoryDueAsync(workspace);
        Api.Ai.Respond = _ => FakeAiAnswer.Say("Riassunto segreto.");
        await Api.RunSummariesAsync();
        Assert.Equal("Riassunto segreto.", (await owner.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversation}/memory")).GetProperty("summary").GetString());

        var (stranger, _) = await OwnerAsync();
        var seen = await stranger.Http.GetFromJsonAsync<JsonElement>($"/api/v1/conversations/{conversation}/memory");
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("summary").ValueKind);
    }

    [Fact]
    public void Contact_details_and_extra_words_never_stay_in_a_summary()
    {
        var clean = ConversationSummarizer.Clean("Giulia, tel +39 333 123 4567, mail giulia.rossi@example.com, ordine #1003.");
        Assert.Equal("Giulia, tel [removed], mail [removed] ordine #1003.", clean);

        var words = ConversationSummarizer.Clean(string.Join(' ', Enumerable.Repeat("parola", 300)));
        Assert.Equal(ConversationMemory.SummaryMaxWords, words!.Split(' ').Length);

        Assert.Null(ConversationSummarizer.Clean("   "));
    }

    [Fact]
    public void The_newest_messages_fit_the_budget_and_the_last_one_always_goes()
    {
        var at = DateTimeOffset.UnixEpoch;
        var messages = new[]
        {
            new AiHistoryMessage("customer", new string('a', 400), at),
            new AiHistoryMessage("ai", new string('b', 400), at.AddSeconds(1)),
            new AiHistoryMessage("customer", new string('c', 400), at.AddSeconds(2)),
        };

        Assert.Equal([messages[1], messages[2]], ContextBuilder.Recent(messages, 210));
        Assert.Equal([messages[0]], ContextBuilder.Older(messages, 210));
        // Longer than the whole budget: still the message to answer.
        Assert.Equal([messages[2]], ContextBuilder.Recent(messages, 1));
    }

    [Fact]
    public async Task Older_messages_already_in_the_summary_are_not_asked_for_again()
    {
        var (_, workspace) = await OwnerAsync();
        var (conversation, tenant) = await MemoryDueAsync(workspace);
        Api.Ai.Respond = _ => FakeAiAnswer.Say("Riassunto.");
        await Api.RunSummariesAsync();
        Assert.Null((await MemoryAsync(tenant, conversation)).SummaryDueAt);

        var calls = Api.Ai.Calls.Count(IsSummary);
        await Api.RunSummariesAsync();
        Assert.Equal(calls, Api.Ai.Calls.Count(IsSummary));

        await using var scope = TenantScope(tenant);
        var history = await scope.ServiceProvider.GetRequiredService<IAiConversations>().HistoryAsync(conversation, 200, CancellationToken.None);
        var memory = await MemoryAsync(tenant, conversation);
        Assert.False(ContextBuilder.Build(history!.Messages, memory, new AiOptions()).NeedsSummary);
    }

    /// <summary>A conversation long enough that its summary is due; the model answers its turn.</summary>
    private async Task<(Guid Conversation, Guid Tenant)> MemoryDueAsync(string workspace)
    {
        var from = Marker();
        var id = await ReceiveAsync(workspace, from, "Ho scelto la borsa nera");
        for (var i = 0; i < 30; i++)
        {
            await ReceiveAsync(workspace, from, $"{i} {Filler}{Filler}{Filler}");
        }

        Api.Ai.Respond = _ => FakeAiAnswer.Say("Certo!");
        await Api.RunAiAsync();
        var tenant = Guid.Parse(workspace);
        Assert.NotNull((await MemoryAsync(tenant, id)).SummaryDueAt);
        return (id, tenant);
    }

    private async Task<ConversationMemory> MemoryAsync(Guid tenant, Guid conversation)
    {
        await using var scope = TenantScope(tenant);
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>().Memory.AsNoTracking().SingleAsync(m => m.ConversationId == conversation);
    }

    private AsyncServiceScope TenantScope(Guid tenant)
    {
        var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenant);
        return scope;
    }

    private static string Marker() => "#" + Guid.NewGuid().ToString("N")[..8];

    private static bool IsSummary(FakeAiCall call) => call.System.Contains("running summary", StringComparison.Ordinal);

    private static bool Mine(FakeAiCall call, string marker) =>
        call.Messages.Any(m => m.Text?.Contains(marker, StringComparison.Ordinal) == true);

    private async Task<(ApiClient Client, string Workspace)> OwnerAsync()
    {
        var client = Api.CreateApiClient();
        var me = await (await client.SignupAndVerifyAsync(Api.Emails, AuthFixture.NewEmail(), "Almanaam")).Content.ReadFromJsonAsync<JsonElement>();
        return (client, me.GetProperty("workspace").GetProperty("id").GetString()!);
    }

    private async Task<Guid> ReceiveAsync(string workspace, string from, string text)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(Guid.Parse(workspace));
        var result = await scope.ServiceProvider.GetRequiredService<IInboxIngest>()
            .ReceiveAsync(new InboundMessage("website", from, "Giulia Rossi", text, null, Api.Clock.GetUtcNow()), CancellationToken.None);
        return result.ConversationId;
    }
}
