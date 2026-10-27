using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>App connection (app role, subject to RLS).</summary>
    public const string AppConnectionName = "Postgres";

    /// <summary>Migration connection (schema owner). Used only by migration tooling, never by the running app.</summary>
    public const string MigrationsConnectionName = "PostgresMigrations";

    /// <summary>
    /// Registers a module DbContext on the shared database with its own schema, snake_case names,
    /// and the tenant interceptors (RLS session setting, tenant write guard, timestamps).
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string schema)
        where TContext : DbContext
    {
        services.AddTenancy();
        services.TryAddScoped<TenantConnectionInterceptor>();
        services.TryAddScoped<TenantSaveChangesInterceptor>();

        services.AddDbContext<TContext>((serviceProvider, options) =>
        {
            // Read lazily so hosts without a database (e.g. some tests) still start.
            var connectionString = configuration.GetConnectionString(AppConnectionName)
                ?? throw new InvalidOperationException($"Connection string '{AppConnectionName}' is not configured.");

            ConfigureNpgsql(options, connectionString, schema)
                .AddInterceptors(
                    serviceProvider.GetRequiredService<TenantConnectionInterceptor>(),
                    serviceProvider.GetRequiredService<TenantSaveChangesInterceptor>());
        });

        return services;
    }

    /// <summary>Scoped <see cref="TenantContext"/>, also exposed as <see cref="ITenantContext"/>.</summary>
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>Provider settings shared by the app, design-time tooling and tests, so the model is identical.</summary>
    public static DbContextOptionsBuilder ConfigureNpgsql(
        DbContextOptionsBuilder options,
        string connectionString,
        string schema)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", schema))
            .UseSnakeCaseNamingConvention();
    }
}
