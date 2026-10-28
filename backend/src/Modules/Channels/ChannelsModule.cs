using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Channels.Features.Connections;
using Nihasdesk.Modules.Channels.Features.Webhooks;
using Nihasdesk.Modules.Channels.Infrastructure;
using Nihasdesk.Modules.Channels.Infrastructure.Meta;

namespace Nihasdesk.Modules.Channels;

public sealed class ChannelsModule : IModule
{
    public string Name => ChannelsDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ChannelsDbContext>(configuration, ChannelsDbContext.Schema);
        services.AddOptions<ChannelsOptions>().Bind(configuration.GetSection(ChannelsOptions.SectionName));
        services.AddOptions<MetaOptions>()
            .Bind(configuration.GetSection(MetaOptions.SectionName))
            .PostConfigure(options =>
            {
                options.DashboardUrl = configuration["App:DashboardUrl"] ?? string.Empty;
                if (string.IsNullOrEmpty(options.RedirectUri))
                {
                    options.RedirectUri = options.DashboardUrl.TrimEnd('/') + ConnectionEndpoints.CallbackPath;
                }
            });

        services.AddHttpClient<MetaGraphClient>(MetaGraphClient.HttpClientName, (sp, client) =>
            {
                var version = sp.GetRequiredService<IOptions<MetaOptions>>().Value.GraphApiVersion;
                client.BaseAddress = new Uri($"https://graph.facebook.com/{version}/");
                client.Timeout = TimeSpan.FromSeconds(15);
            })
            // URLs can contain Page and customer IDs; tokens never are in URLs (SEC-19).
            .RemoveAllLoggers();

        services.AddMemoryCache();
        services.AddSingleton<PageTokens>();
        services.AddSingleton<ConnectStates>();
        services.AddScoped<ConnectionsHandler>();

        // In the API for now, so dashboards hear about new messages over SignalR (ADR 0017).
        services.AddSingleton<MessengerWebhookProcessor>();
        services.AddSingleton<MessengerSender>();
        if (configuration.GetValue($"{ChannelsOptions.SectionName}:{nameof(ChannelsOptions.BackgroundProcessing)}", true))
        {
            services.AddHostedService(sp => sp.GetRequiredService<MessengerWebhookProcessor>());
            services.AddHostedService(sp => sp.GetRequiredService<MessengerSender>());
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ConnectionEndpoints.Map(endpoints);
        MetaWebhookEndpoints.Map(endpoints);
    }
}
