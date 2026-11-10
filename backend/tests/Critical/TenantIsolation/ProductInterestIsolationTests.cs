using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Contacts.Domain;
using Nihasdesk.Modules.Contacts.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for products of interest (Issue #91 PR 3, ADR 0032 §6): what one business's
/// customers tapped on is never seen or written by another business.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class ProductInterestIsolationTests(ModulesPostgresFixture fixture)
{
    private static readonly DateTimeOffset At = new(2026, 11, 9, 19, 14, 0, TimeSpan.Zero);

    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_interests_even_without_the_EF_filter()
    {
        var productOfB = Guid.NewGuid();
        await RecordAsync(B, fixture.TenantB.ContactId, productOfB);
        await RecordAsync(A, fixture.TenantA.ContactId, Guid.NewGuid());

        await using var scope = fixture.Scope(A);
        var rows = await scope.Get<ContactsDbContext>().ProductInterests.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(rows);
        Assert.All(rows, i => Assert.Equal(A, i.TenantId));
        Assert.Empty(await scope.Get<IContactInterest>().ListAsync(fixture.TenantB.ContactId, CancellationToken.None));
    }

    [Fact]
    public async Task Recording_for_another_tenants_contact_writes_nothing()
    {
        var product = Guid.NewGuid();
        await RecordAsync(A, fixture.TenantB.ContactId, product);

        await using var scope = fixture.Scope(B);
        var rows = await scope.Get<ContactsDbContext>().ProductInterests.IgnoreQueryFilters()
            .Where(i => i.ProductId == product)
            .ToListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Writing_an_interest_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<ContactsDbContext>();

        db.ProductInterests.Add(ProductInterest.Create(B, fixture.TenantB.ContactId, Guid.NewGuid(), InterestAction.Buy, At));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_isolates_the_table()
    {
        await RecordAsync(A, fixture.TenantA.ContactId, Guid.NewGuid());
        await RecordAsync(B, fixture.TenantB.ContactId, Guid.NewGuid());

        await TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "contacts", "product_interests");
    }

    private async Task RecordAsync(Guid tenantId, Guid contactId, Guid productId)
    {
        await using var scope = fixture.Scope(tenantId);
        await scope.Get<IContactInterest>().RecordAsync(contactId, productId, InterestAction.Buy, At, CancellationToken.None);
    }
}
