using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;

namespace Nihasdesk.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public string Name => "identity";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
