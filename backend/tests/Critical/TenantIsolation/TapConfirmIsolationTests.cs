using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.Modules.Inbox.Contracts;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// Issue #91 acceptance (ADR 0032 §4): a Conferma tap can't create an order request in another
/// conversation or another business — it acts only on the receiving conversation's own summary.
/// The AI still never creates a confirmed order (covered by the existing AI order tests).
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class TapConfirmIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Conferma_with_the_hash_of_another_conversations_summary_creates_nothing()
    {
        var x = Guid.NewGuid();
        var y = Guid.NewGuid();
        var hashOfX = await SummaryAsync(A, x);

        await using var scope = fixture.Scope(A);
        var answer = await scope.Get<TapHandler>().AnswerAsync(Tap(y, hashOfX), italian: true, CancellationToken.None);

        Assert.True(answer is null || !answer.Result.StartsWith("order_request", StringComparison.Ordinal));
        Assert.False(await scope.Get<CommerceDbContext>().Orders.IgnoreQueryFilters().AnyAsync(o => o.ConversationId == y || o.ConversationId == x));
    }

    [Fact]
    public async Task Conferma_with_its_own_summary_in_another_workspace_finds_no_summary_and_creates_nothing()
    {
        var conversation = Guid.NewGuid();
        var hashInB = await SummaryAsync(B, conversation);

        // The same conversation id, received by workspace A: B's draft is invisible (RLS + filter).
        await using var scope = fixture.Scope(A);
        var answer = await scope.Get<TapHandler>().AnswerAsync(Tap(conversation, hashInB), italian: true, CancellationToken.None);

        Assert.Null(answer);
        Assert.False(await scope.Get<CommerceDbContext>().Orders.IgnoreQueryFilters().AnyAsync(o => o.ConversationId == conversation));
        await using var inB = fixture.Scope(B);
        Assert.False(await inB.Get<CommerceDbContext>().Orders.AnyAsync(o => o.ConversationId == conversation));
    }

    /// <summary>The last message of <paramref name="conversationId"/> is a Conferma tap carrying <paramref name="hash"/>.</summary>
    private static AiConversation Tap(Guid conversationId, string hash) =>
        new(conversationId, Guid.NewGuid(), "messenger", AiHasReplied: true,
            [new AiHistoryMessage("customer", "✅ Conferma", DateTimeOffset.UtcNow, Guid.NewGuid(), false, new AiTap(RichAction.ConfirmSummary, null, null, hash, FromTeam: false))]);

    /// <summary>A summary waiting for Conferma in <paramref name="tenant"/>'s conversation; returns its hash.</summary>
    private async Task<string> SummaryAsync(Guid tenant, Guid conversationId)
    {
        await using var scope = fixture.Scope(tenant);
        var db = scope.Get<AiDbContext>();
        var draft = OrderDraft.Start(tenant, conversationId, Guid.NewGuid());
        var hash = Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).PadRight(OrderDraft.SummaryHashLength, '0');
        draft.Summarized(
            [new DraftLine(Guid.NewGuid(), Guid.NewGuid(), 1)],
            new DraftSummary(Guid.NewGuid(), "Giulia Rossi", "+39 333 1234567", "Via Roma 12", "07100", "Sassari", "SS", "IT", null),
            hash);
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();
        return hash;
    }
}
