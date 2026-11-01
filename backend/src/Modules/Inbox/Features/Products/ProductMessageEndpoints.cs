using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Inbox.Features.Products;

/// <summary><c>POST /api/v1/conversations/{id}/products</c> (ADR 0024): inbox work for every team member.</summary>
internal static class ProductMessageEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/conversations/{id:guid}/products", async (Guid id, SendProductsRequest request, ProductMessagesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.SendAsync(id, request, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/conversations/{id}/messages", result.Value)
                    : result.Error.ToProblem();
            })
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>()
            .AddEndpointFilter<ValidationFilter<SendProductsRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);
    }
}
