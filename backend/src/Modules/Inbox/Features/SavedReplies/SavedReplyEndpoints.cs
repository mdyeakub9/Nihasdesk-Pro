using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Inbox.Features.SavedReplies;

/// <summary>
/// <c>/api/v1/saved-replies</c> (ADR 0022): every member reads them for the composer; Owner and
/// Admin create, change and delete them (docs/standards/permissions.md, SEC-10).
/// </summary>
internal static class SavedReplyEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var replies = endpoints.MapGroup("/api/v1/saved-replies")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>();
        var manage = new RequireTeamRole("owner", "admin");

        replies.MapGet("/", async (SavedRepliesHandler handler, CancellationToken ct) => Results.Ok(await handler.ListAsync(ct)));

        replies.MapPost("/", async (SavedReplyRequest request, SavedRepliesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.CreateAsync(request, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/saved-replies/{result.Value.Id}", result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<SavedReplyRequest>>();

        replies.MapPut("/{id:guid}", async (Guid id, SavedReplyRequest request, SavedRepliesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.UpdateAsync(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage)
            .AddEndpointFilter<ValidationFilter<SavedReplyRequest>>();

        replies.MapDelete("/{id:guid}", async (Guid id, SavedRepliesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.DeleteAsync(id, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter(manage);
    }
}
