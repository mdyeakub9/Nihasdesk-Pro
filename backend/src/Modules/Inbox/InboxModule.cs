using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;

namespace Nihasdesk.Modules.Inbox;

public sealed class InboxModule : IModule
{
    public string Name => "inbox";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
