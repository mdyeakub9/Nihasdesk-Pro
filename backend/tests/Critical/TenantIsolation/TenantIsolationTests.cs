using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Domain;
using Npgsql;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// CLAUDE.md rule 1: one business must never see or change another business's data. Both layers
/// are tested on a real database: EF query filters/write guard and PostgreSQL Row-Level Security.
/// </summary>
[Collection(PostgresTests.Name)]
public sealed class TenantIsolationTests(PostgresFixture fixture)
{
    private Guid A => fixture.TenantA.TenantId;

    private Guid B => fixture.TenantB.TenantId;

    [Fact]
    public async Task Queries_return_only_the_current_tenants_rows()
    {
        await using var scope = fixture.Scope(A);

        var memberships = await scope.Db.Memberships.ToListAsync();
        var workspaces = await scope.Db.Workspaces.ToListAsync();

        Assert.Equal(2, memberships.Count);
        Assert.All(memberships, m => Assert.Equal(A, m.TenantId));
        Assert.Equal(A, Assert.Single(workspaces).Id);
    }

    [Fact]
    public async Task Database_hides_other_tenants_rows_even_without_the_EF_filter()
    {
        await using var scope = fixture.Scope(A);

        var unfiltered = await scope.Db.Memberships.IgnoreQueryFilters().ToListAsync();
        var rawTenantIds = await scope.Db.Database
            .SqlQuery<Guid>($"SELECT tenant_id AS \"Value\" FROM identity.memberships")
            .ToListAsync();
        var otherWorkspace = await scope.Db.Workspaces.IgnoreQueryFilters().AnyAsync(w => w.Id == B);

        Assert.All(unfiltered, m => Assert.Equal(A, m.TenantId));
        Assert.All(rawTenantIds, id => Assert.Equal(A, id));
        Assert.False(otherWorkspace);
    }

    [Fact]
    public async Task Without_a_tenant_queries_fail_and_the_database_returns_nothing()
    {
        await using var scope = fixture.Scope(tenantId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Db.Memberships.ToListAsync());
        Assert.Empty(await scope.Db.Memberships.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await scope.Db.Workspaces.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Writing_a_row_for_another_tenant_is_refused_before_it_reaches_the_database()
    {
        await using var scope = fixture.Scope(A);

        scope.Db.Memberships.Add(Membership.Create(B, fixture.TenantA.OwnerId, Role.Admin));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_rejects_inserting_a_row_for_another_tenant()
    {
        await using var scope = fixture.Scope(A);

        var error = await Assert.ThrowsAsync<PostgresException>(() => scope.Db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO identity.memberships (id, tenant_id, user_id, role, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {B}, {fixture.TenantA.OwnerId}, 'admin', now(), now())
            """));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Database_does_not_let_a_tenant_update_or_delete_another_tenants_rows()
    {
        await using (var scope = fixture.Scope(A))
        {
            var updated = await scope.Db.Database.ExecuteSqlAsync(
                $"UPDATE identity.memberships SET role = 'member' WHERE tenant_id = {B}");
            var deleted = await scope.Db.Database.ExecuteSqlAsync(
                $"DELETE FROM identity.memberships WHERE tenant_id = {B}");

            Assert.Equal(0, updated);
            Assert.Equal(0, deleted);
        }

        await using var tenantB = fixture.Scope(B);
        Assert.Contains(await tenantB.Db.Memberships.ToListAsync(), m => m.Role == Role.Owner);
    }

    [Fact]
    public async Task Database_rejects_moving_a_row_to_another_tenant()
    {
        await using var scope = fixture.Scope(A);

        var error = await Assert.ThrowsAsync<PostgresException>(() => scope.Db.Database.ExecuteSqlAsync(
            $"UPDATE identity.memberships SET tenant_id = {B} WHERE tenant_id = {A}"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [Fact]
    public async Task Tenant_does_not_leak_to_the_next_user_of_a_pooled_connection()
    {
        await using (var tenantA = fixture.Scope(A))
        {
            Assert.NotEmpty(await tenantA.Db.Memberships.ToListAsync());
        }

        // The pool has one connection, so this scope reuses the one tenant A just used.
        await using var noTenant = fixture.Scope(tenantId: null);
        Assert.Empty(await noTenant.Db.Memberships.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task App_role_is_not_owner_or_superuser_and_cannot_bypass_RLS()
    {
        await using var scope = fixture.Scope(A);

        var role = await scope.Db.Database
            .SqlQuery<RoleFlags>($"""
                SELECT rolsuper AS superuser, rolbypassrls AS bypass_rls,
                       (SELECT tableowner FROM pg_tables WHERE schemaname = 'identity' AND tablename = 'memberships') = current_user AS owns_tables
                FROM pg_roles WHERE rolname = current_user
                """)
            .SingleAsync();

        Assert.False(role.Superuser);
        Assert.False(role.BypassRls);
        Assert.False(role.OwnsTables);
    }

    [Fact]
    public void A_scope_cannot_switch_to_another_tenant()
    {
        var context = new TenantContext();
        context.SetTenant(A);

        Assert.Throws<InvalidOperationException>(() => context.SetTenant(B));
    }

    private sealed record RoleFlags(bool Superuser, bool BypassRls, bool OwnsTables);
}
