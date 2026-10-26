using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Nihasdesk.Infrastructure.Health;

public static class HealthCheckExtensions
{
    /// <summary>Tag for checks that must pass before the app takes traffic.</summary>
    public const string ReadyTag = "ready";

    private static readonly string[] ReadyTags = [ReadyTag];

    /// <summary>
    /// Registers readiness checks for PostgreSQL, Redis and RabbitMQ. Connection strings come from
    /// configuration (environment variables in deployment, SEC-13); a missing one makes the app not ready.
    /// </summary>
    public static IHealthChecksBuilder AddDependencyHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services.AddHealthChecks();

        var postgres = configuration.GetConnectionString("Postgres");
        var redis = configuration.GetConnectionString("Redis");
        var rabbitMq = configuration.GetConnectionString("RabbitMq");

        AddOrMissing(builder, "postgres", postgres, cs => builder.AddNpgSql(cs, name: "postgres", tags: ReadyTags));
        AddOrMissing(builder, "redis", redis, cs => builder.AddRedis(cs, name: "redis", tags: ReadyTags));
        AddOrMissing(builder, "rabbitmq", rabbitMq, cs =>
        {
            // One shared connection for the check; a failed connect is retried on the next check.
            var factory = new ConnectionFactory { Uri = new Uri(cs) };
            services.AddSingleton(_ => factory.CreateConnectionAsync().GetAwaiter().GetResult());
            builder.AddRabbitMQ(sp => sp.GetRequiredService<IConnection>(), name: "rabbitmq", tags: ReadyTags);
        });

        return builder;
    }

    private static void AddOrMissing(
        IHealthChecksBuilder builder,
        string name,
        string? connectionString,
        Action<string> add)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            builder.AddCheck(
                name,
                () => HealthCheckResult.Unhealthy("Connection string is not configured."),
                ReadyTags);
            return;
        }

        add(connectionString);
    }
}
