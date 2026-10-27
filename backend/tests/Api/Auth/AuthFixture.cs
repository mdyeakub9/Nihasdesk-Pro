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
}

[CollectionDefinition(Name)]
public sealed class AuthTests : ICollectionFixture<AuthFixture>
{
    public const string Name = "auth";
}
