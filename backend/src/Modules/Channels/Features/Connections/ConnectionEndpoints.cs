using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Channels.Features.Connections;

/// <summary>
/// <c>/api/v1/channels/*</c>: Owner and Admin manage channel connections (docs/standards/permissions.md).
/// The callback is a top-level browser GET from Facebook; the session cookie (SameSite=Lax) comes
/// along, and the encrypted <c>state</c> stands in for the CSRF token (ADR 0017).
/// </summary>
internal static class ConnectionEndpoints
{
    public const string CallbackPath = "/api/v1/channels/meta/callback";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var channels = endpoints.MapGroup("/api/v1/channels")
            .RequireAuthorization()
            .AddEndpointFilter(new RequireTeamRole("owner", "admin"));

        channels.MapGet("/", async (ConnectionsHandler handler, CancellationToken ct) => Results.Ok(await handler.ListAsync(ct)));

        channels.MapPost("/meta/connect", async (ConnectionsHandler handler, CancellationToken ct) =>
        {
            var result = await handler.StartAsync(ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });

        channels.MapGet("/meta/callback", async (string? code, string? state, string? error, ConnectionsHandler handler, CancellationToken ct) =>
            Results.Redirect(await handler.CallbackAsync(code, state, error, ct)));

        channels.MapDelete("/{id:guid}", async (Guid id, ConnectionsHandler handler, CancellationToken ct) =>
        {
            var result = await handler.DisconnectAsync(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        });
    }
}
