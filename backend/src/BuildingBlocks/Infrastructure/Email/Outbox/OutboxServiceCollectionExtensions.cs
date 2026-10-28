using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nihasdesk.Infrastructure.Email.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>API side: <see cref="IEmailOutbox"/> writes into <typeparamref name="TContext"/>'s outbox table.</summary>
    public static IServiceCollection AddEmailOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IEmailOutbox, EmailOutbox<TContext>>();
        return services;
    }

    /// <summary>Workers side: delivers <typeparamref name="TContext"/>'s outbox through SMTP in the background.</summary>
    public static IServiceCollection AddEmailOutboxDispatcher<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        services.AddEmail(configuration);
        services.AddOptions<EmailOutboxOptions>().Bind(configuration.GetSection(EmailOutboxOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<EmailOutboxDispatcher<TContext>>();
        services.AddHostedService(sp => sp.GetRequiredService<EmailOutboxDispatcher<TContext>>());
        return services;
    }
}
