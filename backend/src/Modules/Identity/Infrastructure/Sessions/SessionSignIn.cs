using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Features;
using Nihasdesk.Modules.Identity.Features.Me;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Infrastructure.Sessions;

/// <summary>
/// Starts a server-side session for a verified user in their workspace and returns <see cref="MeResponse"/>.
/// The tenant comes from the user's own memberships, never from the request (CLAUDE.md rule 1).
/// </summary>
internal sealed class SessionSignIn(
    SignInManager<User> signInManager,
    IdentityModuleDbContext db,
    TenantContext tenantContext,
    TimeProvider timeProvider)
{
    public async Task<Result<MeResponse>> SignInAsync(User user, bool remember, CancellationToken cancellationToken)
    {
        // Cross-tenant by design, through the reviewed SECURITY DEFINER function only (AddSessions migration).
        var memberships = await db.Database
            .SqlQuery<MembershipRow>($"SELECT tenant_id, role FROM identity.user_memberships({user.Id})")
            .ToListAsync(cancellationToken);

        // Phase 1: one workspace per user; the first membership is the active workspace.
        if (memberships.FirstOrDefault() is not { } membership)
        {
            return AuthErrors.NoWorkspace;
        }

        var properties = new AuthenticationProperties
        {
            IsPersistent = remember,
            ExpiresUtc = timeProvider.GetUtcNow() + SessionLifetime.Absolute(remember),
            AllowRefresh = false,
        };
        await signInManager.SignInWithClaimsAsync(
            user,
            properties,
            [new Claim(TenantClaims.TenantId, membership.TenantId.ToString())]);

        tenantContext.SetTenant(membership.TenantId);
        var workspace = await db.Workspaces.SingleAsync(cancellationToken);

        return Result.Success(new MeResponse(
            new MeUser(user.Id, user.DisplayName, user.Email!, user.Language, user.EmailConfirmed),
            new MeWorkspace(workspace.Id, workspace.Name),
            membership.Role));
    }

    private sealed record MembershipRow(Guid TenantId, string Role);
}
