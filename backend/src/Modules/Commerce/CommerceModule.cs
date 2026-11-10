using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Modules;
using Nihasdesk.Infrastructure.Persistence;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Modules.Commerce.Contracts;
using Nihasdesk.Modules.Commerce.Features.Images;
using Nihasdesk.Modules.Commerce.Features.Import;
using Nihasdesk.Modules.Commerce.Features.Orders;
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
        services.AddScoped<OrderPricer>();
        services.AddScoped<OrdersHandler>();
        services.AddScoped<IOrderRequests, OrderRequestsService>();
        services.AddScoped<IOrderDetailsCheck, OrderRequestsService>();
        services.AddScoped<IContactOrders, ContactOrdersService>();
        services.AddScoped<IValidator<OrderRequest>, OrderValidator>();
        services.AddScoped<IValidator<RejectRequest>, RejectValidator>();
        services.AddScoped<IValidator<QuoteRequest>, QuoteValidator>();
        AddImages(services, configuration);
    }

    /// <summary>Product images (ADR 0031 §7): the editor's uploads and the signed public links.</summary>
    private static void AddImages(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ProductImageOptions>()
            .Bind(configuration.GetSection(ProductImageOptions.SectionName))
            .Configure(options => options.PublicUrl = configuration["App:PublicUrl"])
            .Validate(options => ProductImageOptions.IsValidKey(options.SigningKey), "Commerce:Images:SigningKey must be base64 of at least 32 bytes.")
            .ValidateOnStart();
        services.AddFileStore(configuration);
        services.AddSingleton<ProductImageSigner>();
        services.AddScoped<IProductImageLinks, ProductImageLinks>();
        services.AddScoped<ProductImagesHandler>();
        services.AddScoped<IValidator<ReorderImagesRequest>, ReorderImagesValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProductEndpoints.Map(endpoints);
        ProductImageEndpoints.Map(endpoints);
        OrderEndpoints.Map(endpoints);
    }
}
