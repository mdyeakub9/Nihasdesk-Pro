using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.AiAgent.Agent;
using Nihasdesk.Modules.AiAgent.Features.Settings;
using Nihasdesk.Modules.Identity.Contracts;

namespace Nihasdesk.Modules.AiAgent.Features.Assistant;

/// <summary><see cref="Style"/>: <c>default</c>, <c>shorter</c> or <c>friendlier</c>. <see cref="Language"/>: the dashboard's, for the summary.</summary>
internal sealed record AssistRequest(string? Style, string? Language);

internal sealed record AssistResponse(
    string? Summary,
    string? Reply,
    IReadOnlyList<TestSource> Sources,
    // price_not_from_tool, link_in_reply, … (SEC-38) or no_reply; null when Reply is set.
    string? Blocked,
    string? Failed);

internal sealed class AssistValidator : AbstractValidator<AssistRequest>
{
    public static readonly string[] Styles = ["default", "shorter", "friendlier"];

    public AssistValidator()
    {
        RuleFor(r => r.Style).Must(s => Styles.Contains(s, StringComparer.Ordinal)).When(r => r.Style is not null)
            .WithMessage("Style must be default, shorter or friendlier.");
        RuleFor(r => r.Language).Must(l => l is "en" or "it").When(r => r.Language is not null)
            .WithMessage("Language must be en or it.");
    }
}

/// <summary>
/// <c>POST /api/v1/conversations/{id}/assist</c> (ADR 0023): inbox work for every team member.
/// Returns a draft; sending stays the composer's normal Send.
/// </summary>
internal static class AssistantEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/conversations/{id:guid}/assist", async (Guid id, AssistRequest request, SuggestionAgent agent, CancellationToken ct) =>
            {
                var result = await agent.SuggestAsync(id, request.Style ?? "default", request.Language ?? "en", ct);
                if (!result.IsSuccess)
                {
                    return result.Error.ToProblem();
                }

                var s = result.Value;
                return Results.Ok(new AssistResponse(s.Summary, s.Reply, s.Sources, s.Blocked, s.Failed));
            })
            .RequireAuthorization()
            .AddEndpointFilter<RequireTeamMember>()
            .AddEndpointFilter<ValidationFilter<AssistRequest>>()
            .RequireRateLimiting(RatePolicies.Messages);
    }
}
