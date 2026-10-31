using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Inbox.Features.Conversations;

namespace Nihasdesk.Modules.Inbox.Features.Notes;

/// <summary><c>/api/v1/conversations/{id}/notes/*</c>: inbox work for every team member (docs/standards/permissions.md).</summary>
internal static class NoteEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var notes = endpoints.MapGroup("/api/v1/conversations/{id:guid}/notes")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>();

        notes.MapGet("/", async (Guid id, NotesHandler handler, CancellationToken ct) =>
            await handler.ListAsync(id, ct) is { } list ? Results.Ok(list) : InboxErrors.NotFound.ToProblem());

        notes.MapPost("/", async (Guid id, AddNoteRequest request, NotesHandler handler, CancellationToken ct) =>
            {
                var result = await handler.AddAsync(id, request, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/conversations/{id}/messages/{result.Value.Id}", result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<AddNoteRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);

        notes.MapPut("/{noteId:guid}/pin", (Guid id, Guid noteId, NotesHandler handler, CancellationToken ct) =>
            PinAsync(id, noteId, pinned: true, handler, ct));

        notes.MapDelete("/{noteId:guid}/pin", (Guid id, Guid noteId, NotesHandler handler, CancellationToken ct) =>
            PinAsync(id, noteId, pinned: false, handler, ct));
    }

    private static async Task<IResult> PinAsync(Guid id, Guid noteId, bool pinned, NotesHandler handler, CancellationToken ct)
    {
        var result = await handler.SetPinnedAsync(id, noteId, pinned, ct);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
    }
}
