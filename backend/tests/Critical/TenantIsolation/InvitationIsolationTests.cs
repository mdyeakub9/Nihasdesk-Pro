using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Domain;
using Npgsql;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>CLAUDE.md rule 1 for invitations: one workspace never sees or writes another's (ADR 0014).</summary>
[Collection(PostgresTests.Name)]
public sealed class InvitationIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Invitations_are_invisible_and_unwritable_across_workspaces()
    {
        var a = fixture.TenantA;
        var b = fixture.TenantB;
        var invitation = Invitation.Create(a.TenantId, "guest@example.com", Role.Member, SHA256.HashData([1, 2, 3]), a.OwnerId, DateTimeOffset.UtcNow);

        await using (var scopeA = fixture.Scope(a.TenantId))
        {
            scopeA.Db.Invitations.Add(invitation);
            await scopeA.Db.SaveChangesAsync();
        }

        await using var scopeB = fixture.Scope(b.TenantId);
        Assert.False(await scopeB.Db.Invitations.AnyAsync(i => i.Id == invitation.Id));
        Assert.False(await scopeB.Db.Invitations.IgnoreQueryFilters().AnyAsync(i => i.Id == invitation.Id));
        Assert.Equal(0, await scopeB.Db.Database.ExecuteSqlAsync(
            $"UPDATE identity.invitations SET revoked_at = now() WHERE id = {invitation.Id}"));

        var insert = await Assert.ThrowsAsync<PostgresException>(() => scopeB.Db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO identity.invitations (id, tenant_id, email, role, token_hash, invited_by, expires_at, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {a.TenantId}, 'x@example.com', 'admin', {SHA256.HashData([9])}, {b.OwnerId}, now(), now(), now())
            """));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);
    }

    [Fact]
    public async Task Token_lookup_function_reveals_only_the_matching_invitation()
    {
        var a = fixture.TenantA;
        var hash = SHA256.HashData([4, 5, 6]);
        await using (var scopeA = fixture.Scope(a.TenantId))
        {
            scopeA.Db.Invitations.Add(Invitation.Create(a.TenantId, "lookup@example.com", Role.Admin, hash, a.OwnerId, DateTimeOffset.UtcNow));
            await scopeA.Db.SaveChangesAsync();
        }

        // Without any tenant, as the public accept endpoint starts.
        await using var anonymous = fixture.Scope(tenantId: null);
        var match = await anonymous.Db.Database
            .SqlQuery<Guid>($"SELECT tenant_id AS value FROM identity.find_invitation({hash})")
            .ToListAsync();
        var none = await anonymous.Db.Database
            .SqlQuery<Guid>($"SELECT tenant_id AS value FROM identity.find_invitation({SHA256.HashData([7])})")
            .ToListAsync();

        Assert.Equal([a.TenantId], match);
        Assert.Empty(none);
        Assert.Empty(await anonymous.Db.Invitations.IgnoreQueryFilters().ToListAsync());
    }
}
