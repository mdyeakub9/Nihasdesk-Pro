using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Infrastructure.Security;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Features.Account;

namespace Nihasdesk.Modules.Identity.Features.Team;

/// <summary>
/// <c>/api/v1/team/*</c> (signed in, permission per endpoint — docs/standards/permissions.md) and
/// the public <c>/api/v1/invitations/*</c> used by the invitation link. Tokens travel in request
/// bodies, never in paths, so they don't appear in request logs (SEC-19).
/// </summary>
internal static class TeamEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var team = endpoints.MapGroup("/api/v1/team").RequireAuthorization();

        team.MapGet("/members", async (TeamHandler handler, CancellationToken ct) => Results.Ok(await handler.MembersAsync(ct)))
            .AddEndpointFilter(new RequirePermission(Permission.TeamView));

        team.MapGet("/invitations", async (TeamHandler handler, CancellationToken ct) => Results.Ok(await handler.InvitationsAsync(ct)))
            .AddEndpointFilter(new RequirePermission(Permission.TeamInvite));

        team.MapPost("/invitations", async (InviteRequest request, TeamHandler handler, CancellationToken ct) =>
            {
                var result = await handler.InviteAsync(request, ct);
                return result.IsSuccess ? Results.Created($"/api/v1/team/invitations/{result.Value.Id}", result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter(new RequirePermission(Permission.TeamInvite))
            .AddEndpointFilter<ValidationFilter<InviteRequest>>()
            .RequireRateLimiting(RatePolicies.Invite);

        team.MapPost("/invitations/{id:guid}/resend", async (Guid id, TeamHandler handler, CancellationToken ct) =>
            {
                var result = await handler.ResendAsync(id, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter(new RequirePermission(Permission.TeamInvite))
            .RequireRateLimiting(RatePolicies.Invite);

        team.MapDelete("/invitations/{id:guid}", async (Guid id, TeamHandler handler, CancellationToken ct) =>
            {
                var result = await handler.RevokeAsync(id, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter(new RequirePermission(Permission.TeamInvite));

        team.MapPatch("/members/{userId:guid}", async (Guid userId, ChangeRoleRequest request, TeamHandler handler, CancellationToken ct) =>
            {
                var result = await handler.ChangeRoleAsync(userId, request, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter(new RequirePermission(Permission.TeamChangeRole))
            .AddEndpointFilter<RequireRecentAuth>()
            .AddEndpointFilter<ValidationFilter<ChangeRoleRequest>>();

        // The role-specific rule (Admins may remove Members only) is checked in the handler.
        team.MapDelete("/members/{userId:guid}", async (Guid userId, TeamHandler handler, CancellationToken ct) =>
            {
                var result = await handler.RemoveAsync(userId, ct);
                return result.IsSuccess ? Results.NoContent() : result.Error.ToProblem();
            })
            .AddEndpointFilter(new RequirePermission(Permission.TeamRemoveMember))
            .AddEndpointFilter<RequireRecentAuth>();

        var invitations = endpoints.MapGroup("/api/v1/invitations").AllowAnonymous().RequireRateLimiting(RatePolicies.Email);

        invitations.MapPost("/preview", async (InvitationTokenRequest request, AcceptInvitationHandler handler, CancellationToken ct) =>
        {
            var result = await handler.PreviewAsync(request.Token ?? string.Empty, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
        });

        invitations.MapPost("/accept", async (AcceptInvitationRequest request, AcceptInvitationHandler handler, CancellationToken ct) =>
            {
                var result = await handler.AcceptAsync(request, ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .AddEndpointFilter<ValidationFilter<AcceptInvitationRequest>>();
    }
}
