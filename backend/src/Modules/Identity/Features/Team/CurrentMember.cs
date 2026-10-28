using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nihasdesk.Infrastructure.Http;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Team;

/// <summary>
/// The signed-in user's membership in the current workspace, read from the database on each
/// request, so role changes and removals apply immediately (docs/standards/permissions.md).
/// </summary>
internal sealed class CurrentMember(IHttpContextAccessor http, IdentityModuleDbContext db)
{
    private Membership? _membership;
    private bool _loaded;

    public Guid UserId =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    public async Task<Membership?> GetAsync(CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            var userId = UserId;
            _membership = userId == Guid.Empty
                ? null
                : await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
            _loaded = true;
        }

        return _membership;
    }
}

/// <summary>Endpoint filter: the current member's role must have <paramref name="permission"/> (SEC-10).</summary>
internal sealed class RequirePermission(Permission permission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var member = context.HttpContext.RequestServices.GetRequiredService<CurrentMember>();
        var membership = await member.GetAsync(context.HttpContext.RequestAborted);
        if (membership is null || !RolePermissions.Has(membership.Role, permission))
        {
            return TeamErrors.Forbidden.ToProblem();
        }

        return await next(context);
    }
}

internal static class TeamErrors
{
    public static readonly Error Forbidden = Error.Forbidden("team.forbidden", "Your role doesn't allow this.");

    /// <summary>Also returned for another workspace's IDs (SEC-06).</summary>
    public static readonly Error NotFound = Error.NotFound("team.not_found", "Not found.");

    public static readonly Error AlreadyMember = Error.Conflict("team.already_member", "This person is already in the team.");

    public static readonly Error OwnerIsFixed =
        Error.Validation("team.owner_is_fixed", "The Owner can't be removed or changed.");

    public static readonly Error InvalidInvitation =
        Error.NotFound("team.invitation_invalid", "This invitation is invalid, already used or expired.");

    public static readonly Error EmailHasAccount = Error.Conflict(
        "team.email_has_account",
        "This email already has a Nihasdesk account. Joining a second workspace comes later.");
}
