using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Features.Import;
using Nihasdesk.Modules.Commerce.Features.Products;
using Nihasdesk.Modules.Commerce.Infrastructure;

namespace Nihasdesk.Modules.Commerce;

public sealed class CommerceModule : IModule
{
    public string Name => CommerceDbContext.Schema;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CommerceDbContext>(configuration, CommerceDbContext.Schema);
        services.AddScoped<ProductsHandler>();
        services.AddScoped<ImportHandler>();
        services.AddScoped<ICatalog, Catalog>();
        services.AddScoped<IValidator<ProductRequest>, ProductValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProductEndpoints.Map(endpoints);
    }
}
