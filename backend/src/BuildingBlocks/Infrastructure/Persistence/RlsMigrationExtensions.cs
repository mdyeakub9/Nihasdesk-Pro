using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Nihasdesk.Infrastructure.Persistence;

/// <summary>
/// Migration helpers for the database side of tenant isolation (SEC-11). Identifiers come from
/// migration code, never from user input, and are still validated and quoted.
/// </summary>
public static partial class RlsMigrationExtensions
{
    /// <summary>Role the API and Workers connect as: not the table owner, cannot bypass RLS.</summary>
    public const string AppRole = "nihasdesk_app";

    /// <summary>
    /// Enables Row-Level Security on a tenant-owned table. Rows are visible and writable only when
    /// <c>tenant_id</c> equals the connection's <c>app.tenant_id</c>; an unset tenant matches nothing.
    /// </summary>
    public static void EnableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        string schema,
        string table,
        string tenantColumn = "tenant_id")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        var qualified = $"{Quote(schema)}.{Quote(table)}";
        var condition =
            $"{Quote(tenantColumn)} = NULLIF(current_setting('{TenantConnectionInterceptor.SettingName}', true), '')::uuid";

        migrationBuilder.Sql($"ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"CREATE POLICY tenant_isolation ON {qualified} USING ({condition}) WITH CHECK ({condition});");
    }

    /// <summary>
    /// Lets the app role use a module schema: data access on all its tables, including tables that
    /// later migrations create. Schema and table ownership stay with the migration user.
    /// </summary>
    public static void GrantAppRoleAccess(this MigrationBuilder migrationBuilder, string schema)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        var quotedSchema = Quote(schema);

        migrationBuilder.Sql($"GRANT USAGE ON SCHEMA {quotedSchema} TO {AppRole};");
        migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {quotedSchema} TO {AppRole};");
        migrationBuilder.Sql($"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {quotedSchema} TO {AppRole};");
        migrationBuilder.Sql(
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {quotedSchema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppRole};");
        migrationBuilder.Sql(
            $"ALTER DEFAULT PRIVILEGES IN SCHEMA {quotedSchema} GRANT USAGE, SELECT ON SEQUENCES TO {AppRole};");
    }

    private static string Quote(string identifier)
    {
        if (!IdentifierPattern().IsMatch(identifier))
        {
            throw new ArgumentException($"Invalid SQL identifier '{identifier}'.", nameof(identifier));
        }

        return $"\"{identifier}\"";
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$")]
    private static partial Regex IdentifierPattern();
}
