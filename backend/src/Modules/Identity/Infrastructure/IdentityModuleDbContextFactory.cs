using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Tenancy;

namespace Nihasdesk.Modules.Identity.Infrastructure;

/// <summary>
/// Used by <c>dotnet ef</c> (and the migration bundle) with the schema-owner connection. The running
/// app never migrates itself (docs/standards/database.md).
/// </summary>
internal sealed class IdentityModuleDbContextFactory : IDesignTimeDbContextFactory<IdentityModuleDbContext>
{
    public IdentityModuleDbContext CreateDbContext(string[] args) =>
        Create(Environment.GetEnvironmentVariable("ConnectionStrings__" + PersistenceServiceCollectionExtensions.MigrationsConnectionName)
            // Placeholder so `migrations add` works offline; `database update` takes --connection.
            ?? "Host=localhost;Database=nihasdesk;Username=nihasdesk");

    public static IdentityModuleDbContext Create(string migrationsConnectionString)
    {
        var options = new DbContextOptionsBuilder<IdentityModuleDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureNpgsql(options, migrationsConnectionString, IdentityModuleDbContext.Schema);
        return new IdentityModuleDbContext(options.Options, new TenantContext());
    }
}
