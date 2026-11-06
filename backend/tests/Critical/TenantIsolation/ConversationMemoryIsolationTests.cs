using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>CLAUDE.md rule 1 for the running summaries (Issue #54, ADR 0026 §4): one business's customers are never seen by another.</summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class ConversationMemoryIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_summaries_even_without_the_EF_filter()
    {
        await SeedAsync();
        await using var scope = fixture.Scope(A);

        var rows = await scope.Get<AiDbContext>().Memory.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, m => Assert.Equal(A, m.TenantId));
    }

    [Fact]
    public async Task Writing_a_summary_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<AiDbContext>();

        db.Memory.Add(ConversationMemory.Create(B, Guid.NewGuid(), Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_isolates_the_table()
    {
        await SeedAsync();
        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "ai", "conversation_memory");
    }

    private async Task SeedAsync()
    {
        foreach (var tenant in new[] { A, B })
        {
            await using var scope = fixture.Scope(tenant);
            var db = scope.Get<AiDbContext>();
            if (!await db.Memory.AnyAsync())
            {
                var memory = ConversationMemory.Create(tenant, Guid.NewGuid(), Guid.NewGuid());
                memory.Cover("Vuole la borsa nera.", DateTimeOffset.UnixEpoch, Guid.NewGuid());
                db.Memory.Add(memory);
                await db.SaveChangesAsync();
            }
        }
    }
}
