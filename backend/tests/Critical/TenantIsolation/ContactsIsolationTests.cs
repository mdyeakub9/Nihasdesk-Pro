using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Contacts.Contracts;
using Nihasdesk.Modules.Contacts.Domain;
using Nihasdesk.Modules.Contacts.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>CLAUDE.md rule 1 for the Contacts module: a business's customers stay its own.</summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class ContactsIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_rows_even_without_the_EF_filter()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<ContactsDbContext>();

        Assert.All(await db.Contacts.ToListAsync(), c => Assert.Equal(A, c.TenantId));
        Assert.All(await db.Identities.ToListAsync(), i => Assert.Equal(A, i.TenantId));
        Assert.All(await db.Contacts.IgnoreQueryFilters().ToListAsync(), c => Assert.Equal(A, c.TenantId));
        Assert.All(await db.Identities.IgnoreQueryFilters().ToListAsync(), i => Assert.Equal(A, i.TenantId));
        Assert.Null(await db.Contacts.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.Id == fixture.TenantB.ContactId));
    }

    [Fact]
    public async Task Another_tenants_contact_cannot_be_read_through_the_contract()
    {
        await using var scope = fixture.Scope(A);
        var directory = scope.Get<IContactDirectory>();

        Assert.Empty(await directory.GetAsync([fixture.TenantB.ContactId], CancellationToken.None));
        Assert.Null(await directory.FindChannelIdAsync(fixture.TenantB.ContactId, ChannelKind.Messenger, CancellationToken.None));
    }

    [Fact]
    public async Task Writing_a_row_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<ContactsDbContext>();

        db.Contacts.Add(Contact.Create(B, "Intruder"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("contacts")]
    [InlineData("contact_identities")]
    public Task Database_isolates_the_table(string table) =>
        TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "contacts", table);
}
