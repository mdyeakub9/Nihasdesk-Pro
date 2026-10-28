using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Channels.Infrastructure;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.Modules.Contacts.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Inbox.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Nihasdesk.TestSupport;

/// <summary>
/// A throwaway PostgreSQL like production: migrated as the owner, used by the app as the
/// non-owner <c>nihasdesk_app</c> role (SEC-11). Needs Docker.
/// </summary>
public sealed class PostgresDatabase : IAsyncDisposable
{
    private const string AppRolePassword = "tests-only-app";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("nihasdesk")
        .WithUsername("nihasdesk")
        .WithPassword("tests-only-owner")
        .Build();

    public string AppConnectionString { get; private set; } = string.Empty;

    public static async Task<PostgresDatabase> StartAsync()
    {
        var database = new PostgresDatabase();
        await database._container.StartAsync();
        var owner = database._container.GetConnectionString();

        await using (var connection = new NpgsqlConnection(owner))
        {
            await connection.OpenAsync();
            await using var createRole = new NpgsqlCommand(
                $"CREATE ROLE {RlsMigrationExtensions.AppRole} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS",
                connection);
            await createRole.ExecuteNonQueryAsync();
        }

        await using (var migrator = IdentityModuleDbContextFactory.Create(owner))
        {
            await migrator.Database.MigrateAsync();
        }

        await using (var contacts = ContactsDbContextFactory.Create(owner))
        {
            await contacts.Database.MigrateAsync();
        }

        await using (var inbox = InboxDbContextFactory.Create(owner))
        {
            await inbox.Database.MigrateAsync();
        }

        await using (var channels = ChannelsDbContextFactory.Create(owner))
        {
            await channels.Database.MigrateAsync();
        }

        await using (var commerce = CommerceDbContextFactory.Create(owner))
        {
            await commerce.Database.MigrateAsync();
        }

        database.AppConnectionString = new NpgsqlConnectionStringBuilder(owner)
        {
            Username = RlsMigrationExtensions.AppRole,
            Password = AppRolePassword,
        }.ConnectionString;

        return database;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
