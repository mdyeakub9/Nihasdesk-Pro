using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Infrastructure.Storage;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Commerce.Infrastructure;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Commerce.Features.Images;

/// <summary>
/// <c>/api/v1/products/{id}/images</c> (ADR 0031 §7): every member sees the photos, Owner and Admin
/// change them (<c>catalog.manage</c>, SEC-10). <c>/media/p/{token}</c> is the signed public link.
/// </summary>
internal static class ProductImageEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var images = endpoints.MapGroup("/api/v1/products/{productId:guid}/images")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        // The form is read by hand (not bound to IFormFile), so the API's own CSRF check applies (SEC-03).
        images.MapPost("/", async (Guid productId, HttpRequest request, ProductImagesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.UploadAsync(productId, request, ct);
                return result.IsSuccess
                    ? Results.Created(ProductImagesHandler.UrlOf(productId, result.Value.Id), result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage)
            .WithMetadata(new RequestSizeLimitAttribute(ProductImagesHandler.RequestMaxBytes))
            .RequireRateLimiting(RatePolicies.ProductImages);

        images.MapPut("/order", async (Guid productId, ReorderImagesRequest request, ProductImagesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.ReorderAsync(productId, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<ReorderImagesRequest>>();

        images.MapDelete("/{imageId:guid}", async (Guid productId, Guid imageId, ProductImagesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.DeleteAsync(productId, imageId, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage);

        images.MapGet("/{imageId:guid}", async (Guid productId, Guid imageId, string? size, ProductImagesHandler handler, HttpResponse response, CancellationToken ct) =>
        {
            if (size is not (null or ProductImagesHandler.Thumbnail))
            {
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["size"] = ["Size must be thumbnail, or left out."] },
                    extensions: new Dictionary<string, object?> { ["code"] = "validation.failed" });
            }

            if (await handler.OpenAsync(productId, imageId, size == ProductImagesHandler.Thumbnail, ct) is not { } file)
            {
                return ImageErrors.NotFound.ToProblem();
            }

            Headers(response, "private, max-age=3600");
            return Results.File(file.Content, file.ContentType);
        });

        endpoints.MapGet(ProductImageSigner.PathPrefix + "{token}", PublicAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RatePolicies.PublicMedia);
    }

    /// <summary>
    /// The signed link Meta fetches for product cards (ADR 0031 §7): card-size copies of saved images
    /// of active products only. It reads <c>commerce.product_images</c> and nothing else, so no token
    /// opens customer media. Every refusal looks the same.
    /// </summary>
    private static async Task<IResult> PublicAsync(
        string token,
        ProductImageSigner signer,
        IServiceScopeFactory scopes,
        IFileStore files,
        TimeProvider time,
        HttpResponse response,
        CancellationToken ct)
    {
        var image = await FindPublicAsync(token, signer, scopes, time.GetUtcNow(), ct);
        if (await ProductImagesHandler.OpenAsync(files, image?.CardKey, image?.ContentType, ct) is not { } file)
        {
            return ImageErrors.NotFound.ToProblem();
        }

        // A shop's product photo, meant to be seen: shared caches may keep it for a day.
        Headers(response, "public, max-age=86400");
        return Results.File(file.Content, file.ContentType);
    }

    /// <summary>
    /// What a public token opens: the card copy of a saved image of an active product, in the signed
    /// workspace, or null. Its own scope: the workspace is the one we signed, whoever is (or isn't)
    /// signed in in this browser; RLS applies as for any request.
    /// </summary>
    internal static async Task<(string? CardKey, string? ContentType)?> FindPublicAsync(
        string token,
        ProductImageSigner signer,
        IServiceScopeFactory scopes,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!signer.TryVerify(token, now, out var tenantId, out var imageId))
        {
            return null;
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenant(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        var image = await db.ProductImages.AsNoTracking()
            .Where(i => i.Id == imageId && i.Status == ProductImageStatus.Saved)
            .Join(db.Products.Where(p => p.Status == ProductStatus.Active), i => i.ProductId, p => p.Id, (i, _) => new { i.CardKey, i.ContentType })
            .SingleOrDefaultAsync(ct);
        return image is null ? null : (image.CardKey, image.ContentType);
    }

    private static void Headers(HttpResponse response, string cacheControl)
    {
        var headers = response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        headers.ContentDisposition = "inline";
        headers.CacheControl = cacheControl;
    }
}
