using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.AiAgent.Domain;
using Nihasdesk.Modules.AiAgent.Infrastructure;
using Nihasdesk.Modules.AiAgent.Ingestion;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for what the AI reads (Issue #53, ADR 0026 §3): search finds only the current
/// workspace's sections, only from sources in use, and never excluded ones.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class KnowledgeSearchIsolationTests(ModulesPostgresFixture fixture)
{
    private const string Word = "zafferano";

    [Fact]
    public async Task Search_never_returns_another_workspaces_or_unused_or_excluded_sections()
    {
        var a = fixture.TenantA.TenantId;
        var b = fixture.TenantB.TenantId;
        var inUseA = await SeedAsync(a, "uso-a", use: true, excluded: false);
        var inUseB = await SeedAsync(b, "uso-b", use: true, excluded: false);
        var notUsedA = await SeedAsync(a, "bozza-a", use: false, excluded: false);
        var excludedA = await SeedAsync(a, "esclusa-a", use: true, excluded: true);

        await using var scope = fixture.Scope(a);
        var search = scope.Get<KnowledgeSearch>();

        var live = await search.SearchAsync(Word, SearchScope.Live, null, CancellationToken.None);
        Assert.Contains(live, p => p.SourceId == inUseA);
        Assert.DoesNotContain(live, p => p.SourceId == inUseB || p.SourceId == notUsedA || p.SourceId == excludedA);

        // The owner's test box sees what is being reviewed, but still only this workspace.
        var review = await search.SearchAsync(Word, SearchScope.LiveAndReview, null, CancellationToken.None);
        Assert.Contains(review, p => p.SourceId == notUsedA);
        Assert.DoesNotContain(review, p => p.SourceId == inUseB || p.SourceId == excludedA);

        // Naming another workspace's source finds nothing.
        Assert.Empty(await search.SearchAsync(Word, SearchScope.LiveAndReview, inUseB, CancellationToken.None));
    }

    private async Task<Guid> SeedAsync(Guid tenant, string name, bool use, bool excluded)
    {
        await using var scope = fixture.Scope(tenant);
        var db = scope.Get<AiDbContext>();
        var origin = $"https://shop.example/{name}";
        var existing = await db.Sources.FirstOrDefaultAsync(s => s.Origin == origin);
        if (existing is not null)
        {
            return existing.Id;
        }

        var source = KnowledgeSource.FromWebPage(tenant, origin, Guid.NewGuid());
        source.ReadDone(null, null, [], DateTimeOffset.UtcNow);
        if (use)
        {
            source.Use();
        }

        var section = KnowledgeSection.Create(tenant, source.Id, 0, null, null, SectionKind.Text, $"{Word} {name}", 2, null);
        section.SetExcluded(excluded);
        db.Sources.Add(source);
        db.Sections.Add(section);
        await db.SaveChangesAsync();
        await scope.Get<SectionIndex>().IndexKeywordsAsync(source.Id, CancellationToken.None);
        return source.Id;
    }
}
