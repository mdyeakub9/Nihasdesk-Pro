using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;

namespace Nihasdesk.Modules.Shipping;

public sealed class ShippingModule : IModule
{
    public string Name => "shipping";

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
