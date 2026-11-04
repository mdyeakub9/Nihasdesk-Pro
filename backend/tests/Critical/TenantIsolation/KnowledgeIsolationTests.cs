using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for imported knowledge (Issue #51, ADR 0026 §1): one business's documents and
/// their sections are never found by, or written for, another.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class KnowledgeIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_rows_even_without_the_EF_filter()
    {
        await SeedAsync();
        await using var scope = fixture.Scope(A);
        var db = scope.Get<AiDbContext>();

        var sources = await db.Sources.IgnoreQueryFilters().ToListAsync();
        var sections = await db.Sections.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(sources);
        Assert.NotEmpty(sections);
        Assert.All(sources, s => Assert.Equal(A, s.TenantId));
        Assert.All(sections, s => Assert.Equal(A, s.TenantId));
    }

    [Fact]
    public async Task Writing_a_section_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        var (_, sectionSource) = await SeedAsync();
        await using var scope = fixture.Scope(A);
        var db = scope.Get<AiDbContext>();

        db.Sections.Add(KnowledgeSection.Create(B, sectionSource, 99, null, null, SectionKind.Text, "Intruder", 1, "en"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("knowledge_sources")]
    [InlineData("knowledge_sections")]
    public async Task Database_isolates_the_table(string table)
    {
        await SeedAsync();
        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "ai", table);
    }

    /// <summary>A source with a section in both workspaces, once per test run; returns tenant A's source.</summary>
    private async Task<(Guid B, Guid SourceOfA)> SeedAsync()
    {
        var sourceOfA = await SeedTenantAsync(A);
        var sourceOfB = await SeedTenantAsync(B);
        return (sourceOfB, sourceOfA);
    }

    private async Task<Guid> SeedTenantAsync(Guid tenant)
    {
        await using var scope = fixture.Scope(tenant);
        var db = scope.Get<AiDbContext>();
        var existing = await db.Sources.FirstOrDefaultAsync();
        if (existing is not null)
        {
            return existing.Id;
        }

        var source = KnowledgeSource.FromWebPage(tenant, "https://shop.example/spedizioni", Guid.NewGuid());
        db.Sources.Add(source);
        db.Sections.Add(KnowledgeSection.Create(tenant, source.Id, 0, "Spedizioni", null, SectionKind.Text, "Spediamo in tutta Italia.", 6, "it"));
        await db.SaveChangesAsync();
        return source.Id;
    }
}
