using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Infrastructure;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// SEC-11 for tables that don't exist yet: every table with a <c>tenant_id</c>, in every module
/// schema, must have Row-Level Security and its tenant policy. A new table without them fails here.
/// </summary>
[Collection(ModulesPostgresTests.Name)]
public sealed class RlsCoverageTests(ModulesPostgresFixture fixture)
{
    /// <summary>
    /// The only tenant_id tables without the policy, each for a reason recorded in ADR 0027.
    /// Adding to this list needs the owner's approval and an ADR, like any change to this folder.
    /// </summary>
    private static readonly string[] WithoutTenantPolicy =
    [
        // Read by cookie before any tenant is known; always looked up by session and user ID (ADR 0010).
        "identity.sessions",
        // Also holds events without a tenant (e.g. auth.login_failed); append-only for the app (ADR 0012, SEC-47).
        "identity.audit_events",
    ];

    [Fact]
    public async Task Every_table_with_a_tenant_id_has_row_level_security_and_the_tenant_policy()
    {
        await using var scope = fixture.Scope(tenantId: null);

        var tables = await scope.Get<IdentityModuleDbContext>().Database
            .SqlQuery<TableRls>($"""
                SELECT n.nspname || '.' || c.relname AS name,
                       c.relrowsecurity AS rls_enabled,
                       EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid AND p.polname = 'tenant_isolation') AS has_policy
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                JOIN pg_attribute a ON a.attrelid = c.oid AND a.attname = 'tenant_id' AND NOT a.attisdropped
                WHERE c.relkind IN ('r', 'p') AND n.nspname NOT IN ('pg_catalog', 'information_schema')
                ORDER BY 1
                """)
            .ToListAsync();

        // Not vacuous: all the module tables the isolation tests cover are found.
        Assert.True(tables.Count >= 19, $"Only {tables.Count} tenant tables found.");
        Assert.Contains(tables, t => t.Name == "commerce.orders");
        Assert.Empty(tables.Where(t => (!t.RlsEnabled || !t.HasPolicy) && !WithoutTenantPolicy.Contains(t.Name)).Select(t => t.Name));
    }

    [Fact]
    public async Task The_exceptions_still_exist_so_the_list_cannot_grow_stale()
    {
        await using var scope = fixture.Scope(tenantId: null);

        var names = await scope.Get<IdentityModuleDbContext>().Database
            .SqlQuery<string>($"""
                SELECT n.nspname || '.' || c.relname AS "Value"
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE c.relkind IN ('r', 'p')
                """)
            .ToListAsync();

        Assert.All(WithoutTenantPolicy, name => Assert.Contains(name, names));
    }

    private sealed record TableRls(string Name, bool RlsEnabled, bool HasPolicy);
}
