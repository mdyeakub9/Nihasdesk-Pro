using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;

namespace Nihasdesk.Modules.Channels;

public sealed class ChannelsModule : IModule
{
    public string Name => "channels";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
