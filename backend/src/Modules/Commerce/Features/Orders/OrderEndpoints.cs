using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.Commerce.Domain;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Commerce.Features.Orders;

/// <summary>
/// <c>/api/v1/orders</c> (PRD 4.9, ADR 0025): order work is inbox work, so every team member may see,
/// create, approve and reject orders (docs/standards/permissions.md). Approvals are audited (SEC-47).
/// </summary>
internal static class OrderEndpoints
{
    private static readonly Dictionary<string, OrderStatus> Statuses = new(StringComparer.Ordinal)
    {
        ["pending"] = OrderStatus.Pending,
        ["confirmed"] = OrderStatus.Confirmed,
        ["rejected"] = OrderStatus.Rejected,
    };

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var orders = endpoints.MapGroup("/api/v1/orders")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>();

        orders.MapGet("/", async (string? status, Guid? conversationId, string? cursor, int? limit, OrdersHandler handler, CancellationToken ct) =>
        {
            if ((status is not null && !Statuses.ContainsKey(status))
                || (cursor is not null && !OrdersHandler.TryDecode(cursor, out _))
                || limit is < 1 or > OrderValues.MaxPageSize)
            {
                return Error.Validation("validation.failed", "Status must be pending, confirmed or rejected; check cursor and limit.").ToProblem();
            }

            return Results.Ok(await handler.ListAsync(status is null ? null : Statuses[status], conversationId, cursor, limit, ct));
        });

        orders.MapGet("/counts", async (OrdersHandler handler, CancellationToken ct) => Results.Ok(await handler.CountsAsync(ct)));

        // A preview for the Create order form: prices by code, nothing saved (SEC-41).
        orders.MapPost("/quote", async (QuoteRequest request, OrdersHandler handler, CancellationToken ct) =>
            {
                var result = await handler.QuoteAsync(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<QuoteRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);

        orders.MapGet("/{id:guid}", async (Guid id, OrdersHandler handler, CancellationToken ct) =>
            await handler.GetAsync(id, ct) is { } order ? Results.Ok(order) : OrderErrors.NotFound.ToProblem());

        orders.MapPost("/", async (OrderRequest request, OrdersHandler handler, CancellationToken ct) =>
            {
                var result = await handler.CreateAsync(request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/orders/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<OrderRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);

        orders.MapPut("/{id:guid}", async (Guid id, OrderRequest request, OrdersHandler handler, CancellationToken ct) =>
            {
                var result = await handler.UpdateAsync(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<OrderRequest>>();

        orders.MapPost("/{id:guid}/approve", async (Guid id, OrdersHandler handler, CancellationToken ct) =>
        {
            var result = await handler.ApproveAsync(id, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });

        orders.MapPost("/{id:guid}/reject", async (Guid id, RejectRequest request, OrdersHandler handler, CancellationToken ct) =>
            {
                var result = await handler.RejectAsync(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<RejectRequest>>();
    }
}
