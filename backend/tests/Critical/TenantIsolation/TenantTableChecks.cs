using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Infrastructure;
using Npgsql;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// The database layer of tenant isolation (SEC-11) for one table, with no EF filter in the way:
/// raw SQL as the app role, the way a missed filter or a hand-written query would run.
/// </summary>
internal static partial class TenantTableChecks
{
    public static async Task AssertRowLevelSecurityAsync(ModulesPostgresFixture fixture, string schema, string table)
    {
        var qualified = Qualified(schema, table);
        var a = fixture.TenantA.TenantId;
        var b = fixture.TenantB.TenantId;

        await using (var scope = fixture.Scope(a))
        {
            var db = scope.Get<IdentityModuleDbContext>().Database;

            Assert.True(await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM {qualified}") > 0, $"{qualified} has no seeded rows for tenant A.");
            Assert.Equal(0, await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM {qualified} WHERE tenant_id <> {{0}}", a));

            Assert.Equal(0, await ExecuteAsync(db, $"UPDATE {qualified} SET tenant_id = tenant_id WHERE tenant_id = {{0}}", b));
            Assert.Equal(0, await ExecuteAsync(db, $"DELETE FROM {qualified} WHERE tenant_id = {{0}}", b));

            // A copy of one of A's rows, written for B: refused by the policy's WITH CHECK.
            var insert = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(db,
                $$"""
                CREATE TEMP TABLE critical_copy ON COMMIT DROP AS SELECT * FROM {{qualified}} LIMIT 1;
                UPDATE critical_copy SET id = gen_random_uuid(), tenant_id = {0};
                INSERT INTO {{qualified}} SELECT * FROM critical_copy;
                """,
                b));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, insert.SqlState);

            var move = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(db,
                $"UPDATE {qualified} SET tenant_id = {{0}} WHERE tenant_id = {{1}}", b, a));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, move.SqlState);
        }

        await using (var tenantB = fixture.Scope(b))
        {
            var db = tenantB.Get<IdentityModuleDbContext>().Database;
            Assert.True(await CountAsync(db, $"SELECT count(*)::int AS \"Value\" FROM {qualified}") > 0, $"Tenant B lost rows in {qualified}.");
        }

        await using var noTenant = fixture.Scope(tenantId: null);
        Assert.Equal(0, await CountAsync(noTenant.Get<IdentityModuleDbContext>().Database, $"SELECT count(*)::int AS \"Value\" FROM {qualified}"));
    }

    // The SQL is built from validated identifiers only; values always go in as parameters.
    private static Task<int> ExecuteAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade db, string sql, params object[] parameters) =>
        db.ExecuteSqlRawAsync(sql, parameters);

    private static Task<int> CountAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade db, string sql, params object[] parameters) =>
        db.SqlQueryRaw<int>(sql, parameters).SingleAsync();

    // Table names come from the tests, never from input; still validated before they go into SQL.
    private static string Qualified(string schema, string table)
    {
        Assert.Matches(Identifier(), schema);
        Assert.Matches(Identifier(), table);
        return $"\"{schema}\".\"{table}\"";
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex Identifier();
}
