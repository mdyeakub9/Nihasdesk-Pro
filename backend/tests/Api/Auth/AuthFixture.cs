using Nihasdesk.TestSupport;

namespace Nihasdesk.Api.Tests.Auth;

/// <summary>One database and API for all auth tests; each test uses its own email addresses.</summary>
public sealed class AuthFixture : IAsyncLifetime
{
    private PostgresDatabase? _database;

    public NihasdeskApiFactory Api { get; private set; } = null!;

    internal PostgresDatabase Database => _database!;

    public async Task InitializeAsync()
    {
        _database = await PostgresDatabase.StartAsync();
        Api = new NihasdeskApiFactory(_database);
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    public static string NewEmail() => $"owner-{Guid.NewGuid():N}@example.com";

    /// <summary>Audit actions for a user, oldest first, read as the app role.</summary>
    public async Task<List<(string Action, string? Ip)>> AuditFor(Guid userId)
    {
        await using var connection = new Npgsql.NpgsqlConnection(Database.AppConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT action, ip_address FROM identity.audit_events WHERE user_id = @user ORDER BY occurred_at, id",
            connection);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync();
        var actions = new List<(string, string?)>();
        while (await reader.ReadAsync())
        {
            actions.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return actions;
    }
}

[CollectionDefinition(Name)]
public sealed class AuthTests : ICollectionFixture<AuthFixture>
{
    public const string Name = "auth";
}
