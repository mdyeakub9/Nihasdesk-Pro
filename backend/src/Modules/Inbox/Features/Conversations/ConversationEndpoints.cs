using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.Inbox.Features.Conversations;

/// <summary><c>/api/v1/conversations/*</c>: inbox work for every team member (docs/standards/permissions.md).</summary>
internal static class ConversationEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var conversations = endpoints.MapGroup("/api/v1/conversations")
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>();

        conversations.MapGet("/", async ([AsParameters] ListConversationsQuery query, ConversationsHandler handler, CancellationToken ct) =>
                Results.Ok(await handler.ListAsync(query, ct)))
            .AddEndpointFilter<ValidationFilter<ListConversationsQuery>>();

        conversations.MapGet("/{id:guid}", async (Guid id, ConversationsHandler handler, CancellationToken ct) =>
            await handler.GetAsync(id, ct) is { } conversation ? Results.Ok(conversation) : InboxErrors.NotFound.ToProblem());

        conversations.MapPatch("/{id:guid}", async (Guid id, UpdateConversationRequest request, ConversationsHandler handler, CancellationToken ct) =>
            {
                var result = await handler.UpdateAsync(id, request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<UpdateConversationRequest>>();

        conversations.MapPut("/{id:guid}/assignee", async (Guid id, AssignRequest request, ConversationsHandler handler, CancellationToken ct) =>
        {
            var result = await handler.AssignAsync(id, request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });

        conversations.MapPost("/{id:guid}/read", async (Guid id, ConversationsHandler handler, CancellationToken ct) =>
        {
            var result = await handler.MarkReadAsync(id, ct);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
        });

        // "X is typing…" for the other team members in this conversation (PRD 4.4).
        conversations.MapPost("/{id:guid}/typing", async (Guid id, ConversationsHandler handler, CancellationToken ct) =>
            {
                var result = await handler.TypingAsync(id, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .RequireRateLimiting(RatePolicies.Messages);

        conversations.MapGet("/{id:guid}/messages", async (Guid id, [AsParameters] ListMessagesQuery query, ConversationsHandler handler, CancellationToken ct) =>
                await handler.MessagesAsync(id, query, ct) is { } page ? Results.Ok(page) : InboxErrors.NotFound.ToProblem())
            .AddEndpointFilter<ValidationFilter<ListMessagesQuery>>();

        conversations.MapPost("/{id:guid}/messages", async (Guid id, SendMessageRequest request, ConversationsHandler handler, CancellationToken ct) =>
            {
                var result = await handler.ReplyAsync(id, request, ct);
                return result.IsSuccess
                    ? Results.Created($"/api/v1/conversations/{id}/messages/{result.Value.Id}", result.Value)
                    : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<SendMessageRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);
    }
}
