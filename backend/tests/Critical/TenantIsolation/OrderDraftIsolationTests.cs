using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for order drafts (Issue #91, ADR 0032 §4): what a customer chose with buttons, and
/// the delivery details of a summary, are never visible to or writable by another business.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class OrderDraftIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_drafts_even_without_the_EF_filter()
    {
        await SeedAsync(A);
        await SeedAsync(B);
        await using var scope = fixture.Scope(A);

        var rows = await scope.Get<AiDbContext>().Drafts.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, d => Assert.Equal(A, d.TenantId));
    }

    [Fact]
    public async Task A_draft_of_another_tenant_is_not_found_by_its_conversation()
    {
        var conversationOfB = await SeedAsync(B);
        await using var scope = fixture.Scope(A);

        Assert.Null(await scope.Get<AiDbContext>().Drafts.IgnoreQueryFilters().SingleOrDefaultAsync(d => d.ConversationId == conversationOfB));
    }

    [Fact]
    public async Task Writing_a_draft_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<AiDbContext>();

        db.Drafts.Add(OrderDraft.Start(B, Guid.NewGuid(), Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_isolates_the_table()
    {
        await SeedAsync(A);
        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "ai", "order_drafts");
    }

    private async Task<Guid> SeedAsync(Guid tenant)
    {
        var conversationId = Guid.NewGuid();
        await using var scope = fixture.Scope(tenant);
        var db = scope.Get<AiDbContext>();
        var draft = OrderDraft.Start(tenant, conversationId, Guid.NewGuid());
        draft.AddLine(Guid.NewGuid(), Guid.NewGuid());
        draft.Summarized(
            draft.Lines,
            new DraftSummary(Guid.NewGuid(), "Giulia Rossi", "+39 333 1234567", "Via Roma 12", "07100", "Sassari", "SS", "IT", null),
            new string('a', OrderDraft.SummaryHashLength));
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();
        return conversationId;
    }
}
