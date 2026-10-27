using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Me;

/// <summary>Reads the current user and workspace through the tenant-scoped context.</summary>
internal sealed class MeHandler(IdentityModuleDbContext db)
{
    public async Task<Result<MeResponse>> HandleAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return AuthErrors.InvalidCredentials;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, cancellationToken);
        var workspace = await db.Workspaces.SingleOrDefaultAsync(cancellationToken);
        if (user is null || membership is null || workspace is null)
        {
            return AuthErrors.NoWorkspace;
        }

        return Result.Success(new MeResponse(
            new MeUser(user.Id, user.DisplayName, user.Email!, user.Language, user.EmailConfirmed),
            new MeWorkspace(workspace.Id, workspace.Name),
            membership.Role.ToString().ToLowerInvariant()));
    }
}
