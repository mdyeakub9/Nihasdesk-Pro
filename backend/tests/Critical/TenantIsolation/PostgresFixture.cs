using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Nihasdesk.CriticalTests.TenantIsolation;

/// <summary>
/// A real PostgreSQL (same image as infra/docker-compose.yml) migrated as the schema owner, with
/// the app connecting as the non-owner app role, exactly like production (SEC-11). Two tenants are
/// seeded the way signup will create them.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string AppRolePassword = "critical-tests-only";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .WithDatabase("nihasdesk")
        .WithUsername("nihasdesk")
        .WithPassword("critical-tests-owner")
        .Build();

    private ServiceProvider? _services;

    internal TenantSeed TenantA { get; private set; } = null!;

    internal TenantSeed TenantB { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var ownerConnectionString = _container.GetConnectionString();

        await using (var owner = new NpgsqlConnection(ownerConnectionString))
        {
            await owner.OpenAsync();
            await using var createRole = new NpgsqlCommand(
                $"CREATE ROLE {RlsMigrationExtensions.AppRole} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS",
                owner);
            await createRole.ExecuteNonQueryAsync();
        }

        await using (var migrator = IdentityModuleDbContextFactory.Create(ownerConnectionString))
        {
            await migrator.Database.MigrateAsync();
        }

        // One pooled connection, so tests also prove a tenant never leaks to the next user of it.
        var appConnectionString = new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = RlsMigrationExtensions.AppRole,
            Password = AppRolePassword,
            MaxPoolSize = 1,
        }.ConnectionString;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{PersistenceServiceCollectionExtensions.AppConnectionName}"] = appConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        new IdentityModule().AddServices(services, configuration);
        _services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        TenantA = await SeedTenantAsync("Almanaam", "owner-a@example.com");
        TenantB = await SeedTenantAsync("Bottega Blu", "owner-b@example.com");
    }

    public async Task DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>A request scope; <paramref name="tenantId"/> plays the authenticated tenant.</summary>
    internal TestScope Scope(Guid? tenantId)
    {
        var scope = _services!.CreateAsyncScope();
        if (tenantId is { } id)
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(id);
        }

        return new TestScope(scope);
    }

    private async Task<TenantSeed> SeedTenantAsync(string name, string email)
    {
        var organization = Organization.Create(name);
        var workspace = Workspace.Create(organization.Id, name);
        var user = new User { UserName = email, Email = email, DisplayName = name };
        var member = new User { UserName = "member-" + email, Email = "member-" + email, DisplayName = "Member" };

        await using var scope = Scope(workspace.Id);
        scope.Db.AddRange(organization, workspace, user, member);
        scope.Db.AddRange(
            Membership.Create(workspace.Id, user.Id, Role.Owner),
            Membership.Create(workspace.Id, member.Id, Role.Member));
        await scope.Db.SaveChangesAsync();

        return new TenantSeed(workspace.Id, user.Id);
    }
}

internal sealed record TenantSeed(Guid TenantId, Guid OwnerId);

internal sealed class TestScope(AsyncServiceScope scope) : IAsyncDisposable
{
    public IdentityModuleDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

    public ValueTask DisposeAsync() => scope.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class PostgresTests : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
