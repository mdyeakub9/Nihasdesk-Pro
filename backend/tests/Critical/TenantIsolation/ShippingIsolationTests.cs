using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Shipping.Contracts;
using Nihasdesk.Modules.Shipping.Domain;
using Nihasdesk.Modules.Shipping.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>CLAUDE.md rule 1 for the Shipping module, including the rates the AI quotes from.</summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class ShippingIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_zones_even_without_the_EF_filter()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<ShippingDbContext>();

        Assert.All(await db.Zones.ToListAsync(), z => Assert.Equal(A, z.TenantId));
        Assert.All(await db.Zones.IgnoreQueryFilters().ToListAsync(), z => Assert.Equal(A, z.TenantId));
    }

    [Fact]
    public async Task The_AI_gets_only_the_current_tenants_shipping_rates()
    {
        await using var scope = fixture.Scope(A);

        var rates = await scope.Get<IShippingRates>().ListAsync(CancellationToken.None);

        Assert.NotEmpty(rates);
        Assert.Contains(rates, r => r.Id == fixture.TenantA.ZoneId);
        Assert.DoesNotContain(rates, r => r.Id == fixture.TenantB.ZoneId);
    }

    [Fact]
    public async Task Writing_a_zone_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<ShippingDbContext>();

        db.Zones.Add(ShippingZone.Create(B, 1, "Intruder", null, 100, "EUR", null, null, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public Task Database_isolates_shipping_zones() =>
        TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "shipping", "zones");
}
