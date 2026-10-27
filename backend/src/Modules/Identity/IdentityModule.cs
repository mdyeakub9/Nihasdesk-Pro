using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Identity.Infrastructure;

namespace Nihasdesk.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public string Name => IdentityModuleDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<IdentityModuleDbContext>(configuration, IdentityModuleDbContext.Schema);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
