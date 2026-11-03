using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Channels.Domain;
using Nihasdesk.Modules.Channels.Infrastructure;
using Nihasdesk.Modules.Contacts.Contracts;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1 for the Channels module: a connected Page (and its token) belongs to one
/// workspace. <c>webhook_events</c> is not tenant-owned by design (ADR 0017) and is not tested here.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class ChannelsIsolationTests(ModulesPostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_connections_even_without_the_EF_filter()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<ChannelsDbContext>();

        Assert.All(await db.Connections.ToListAsync(), c => Assert.Equal(A, c.TenantId));
        Assert.All(await db.Connections.IgnoreQueryFilters().ToListAsync(), c => Assert.Equal(A, c.TenantId));
        Assert.Null(await db.Connections.IgnoreQueryFilters().SingleOrDefaultAsync(c => c.Id == fixture.TenantB.ConnectionId));
    }

    [Fact]
    public async Task Writing_a_connection_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);
        var db = scope.Get<ChannelsDbContext>();

        db.Connections.Add(ChannelConnection.Connect(B, ChannelKind.Messenger, "intruder-page", "Intruder", "token", fixture.TenantA.OwnerId, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public Task Database_isolates_channel_connections() =>
        TenantTableChecks.AssertRowLevelSecurityAsync(fixture, "channels", "channel_connections");
}
