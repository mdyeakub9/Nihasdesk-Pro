using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nihasdesk.SharedKernel;
using Npgsql;

namespace Nihasdesk.Infrastructure.Persistence;

/// <summary>
/// Writes the current tenant into the PostgreSQL session setting <c>app.tenant_id</c> every time
/// EF opens a connection, so Row-Level Security policies can filter rows (SEC-11). Without a tenant
/// the setting is empty and the policies return no rows. Npgsql resets session state when a
/// pooled connection is returned, so a tenant never leaks to the next user of the connection.
/// </summary>
internal sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    internal const string SettingName = "app.tenant_id";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private NpgsqlCommand CreateCommand(DbConnection connection)
    {
        // Parameterized; never string-built SQL (SEC-01).
        var command = new NpgsqlCommand("SELECT set_config(@name, @value, false)", (NpgsqlConnection)connection);
        command.Parameters.AddWithValue("name", SettingName);
        command.Parameters.AddWithValue("value", tenantContext.HasTenant ? tenantContext.TenantId.ToString() : string.Empty);
        return command;
    }
}
