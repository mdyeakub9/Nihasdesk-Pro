using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Shipping.Contracts;
using Nihasdesk.Modules.Shipping.Features.Zones;
using Nihasdesk.Modules.Shipping.Infrastructure;

namespace Nihasdesk.Modules.Shipping;

public sealed class ShippingModule : IModule
{
    public string Name => ShippingDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ShippingDbContext>(configuration, ShippingDbContext.Schema);
        services.AddScoped<IShippingRates, ShippingRates>();
        services.AddScoped<IValidator<ZonesRequest>, ZonesValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ZoneEndpoints.Map(endpoints);
    }
}
