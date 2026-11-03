using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for the AiAgent module: one business's AI settings, policies, taught answers
/// and runs are never used for, or visible to, another.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class AiAgentIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_rows_even_without_the_EF_filter()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<AiDbContext>();

        Assert.All(await db.Settings.IgnoreQueryFilters().ToListAsync(), s => Assert.Equal(A, s.TenantId));
        Assert.All(await db.Policies.IgnoreQueryFilters().ToListAsync(), p => Assert.Equal(A, p.TenantId));
        Assert.All(await db.Gaps.IgnoreQueryFilters().ToListAsync(), g => Assert.Equal(A, g.TenantId));
        Assert.All(await db.Answers.IgnoreQueryFilters().ToListAsync(), a => Assert.Equal(A, a.TenantId));
        Assert.All(await db.Runs.IgnoreQueryFilters().ToListAsync(), r => Assert.Equal(A, r.TenantId));
        Assert.Single(await db.Settings.ToListAsync());
        Assert.Single(await db.Policies.ToListAsync());
    }

    [Fact]
    public async Task Writing_a_taught_answer_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<AiDbContext>();

        db.Answers.Add(TaughtAnswer.Create(B, "Sconti?", "No", fixture.TenantA.OwnerId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("shop_policies")]
    [InlineData("knowledge_gaps")]
    [InlineData("taught_answers")]
    [InlineData("runs")]
    public Task Database_isolates_the_table(string table) =>
        TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "ai", table);
}
