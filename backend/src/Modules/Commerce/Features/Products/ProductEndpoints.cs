using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.Commerce.Features.Import;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Commerce.Features.Products;

/// <summary>
/// <c>/api/v1/products/*</c> (ADR 0018). Every member may read the catalog; Owner and Admin change
/// it (<c>catalog.manage</c>, docs/standards/permissions.md).
/// </summary>
internal static class ProductEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var products = endpoints.MapGroup("/api/v1/products")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        products.MapGet("/", async (string? q, string? status, string? cursor, int? limit, ProductsHandler handler, CancellationToken ct) =>
        {
            if (q?.Length > 200
                || (status is not null && !CatalogInput.Statuses.ContainsKey(status))
                || (cursor is not null && !ProductsHandler.TryDecode(cursor, out _))
                || limit is < 1 or > ProductsHandler.MaxPageSize)
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["query"] = ["Invalid search, status, cursor or limit."] },
                    extensions: new Dictionary<string, object?> { ["code"] = "validation.failed" });
            }

            return Results.Ok(await handler.ListAsync(q, status, cursor, limit, ct));
        });

        products.MapGet("/{id:guid}", async (Guid id, ProductsHandler handler, CancellationToken ct) =>
            await handler.GetAsync(id, ct) is { } product ? Results.Ok(product) : CatalogErrors.NotFound.ToProblem());

        products.MapPost("/", async (ProductRequest request, ProductsHandler handler, CancellationToken ct) =>
            {
                var result = await handler.CreateAsync(request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/products/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<ProductRequest>>();

        products.MapPut("/{id:guid}", async (Guid id, ProductRequest request, ProductsHandler handler, CancellationToken ct) =>
            {
                var result = await handler.UpdateAsync(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<ProductRequest>>();

        products.MapGet("/import/template", () =>
                Results.File(Encoding.UTF8.GetBytes(ImportHandler.Template()), "text/csv; charset=utf-8", "nihasdesk-products-template.csv"))
            .AddEndpointFilter(manage);

        // The form is read by hand (not bound to IFormFile), so the API's own CSRF check applies (SEC-03).
        products.MapPost("/import/preview", (HttpRequest request, ImportHandler handler, CancellationToken ct) => ImportAsync(request, handler, apply: false, ct))
            .AddEndpointFilter(manage)
            .WithMetadata(new RequestSizeLimitAttribute(ImportHandler.MaxFileBytes + 64 * 1024))
            .RequireRateLimiting(RatePolicies.Uploads);

        products.MapPost("/import/apply", (HttpRequest request, ImportHandler handler, CancellationToken ct) => ImportAsync(request, handler, apply: true, ct))
            .AddEndpointFilter(manage)
            .WithMetadata(new RequestSizeLimitAttribute(ImportHandler.MaxFileBytes + 64 * 1024))
            .RequireRateLimiting(RatePolicies.Uploads);
    }

    private static async Task<IResult> ImportAsync(HttpRequest request, ImportHandler handler, bool apply, CancellationToken ct)
    {
        var file = await ImportHandler.ReadFileAsync(request, ct);
        if (file.IsFailure)
        {
            return file.Error.ToProblem();
        }

        var result = await handler.RunAsync(file.Value, apply, ct);
        // Apply with issues saved nothing: 422 with the same report the preview gives.
        return apply && !result.Applied ? Results.UnprocessableEntity(result) : Results.Ok(result);
    }
}
