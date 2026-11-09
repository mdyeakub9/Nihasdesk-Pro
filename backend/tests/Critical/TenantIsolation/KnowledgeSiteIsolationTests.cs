using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>CLAUDE.md rule 1 for websites given by domain (Issue #80, ADR 0030): one business never sees another's.</summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class KnowledgeSiteIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_sites_even_without_the_EF_filter()
    {
        await SeedAsync();
        await using var scope = fixture.Scope(A);

        var rows = await scope.Get<AiDbContext>().Sites.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, s => Assert.Equal(A, s.TenantId));
    }

    [Fact]
    public async Task Writing_a_site_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<AiDbContext>();

        db.Sites.Add(KnowledgeSite.Create(B, "intruder.example", "https://intruder.example/", Guid.NewGuid()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_isolates_the_table()
    {
        await SeedAsync();
        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "ai", "knowledge_sites");
    }

    private async Task SeedAsync()
    {
        foreach (var tenant in new[] { A, B })
        {
            await using var scope = fixture.Scope(tenant);
            var db = scope.Get<AiDbContext>();
            if (!await db.Sites.AnyAsync())
            {
                db.Sites.Add(KnowledgeSite.Create(tenant, $"shop-{tenant:N}.example", $"https://shop-{tenant:N}.example/", Guid.NewGuid()));
                await db.SaveChangesAsync();
            }
        }
    }
}
