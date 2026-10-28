using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Accounts;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Features.Me;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Invitations;
using Nihasdesk.Modules.Identity.Infrastructure.Sessions;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Team;

internal sealed record InvitationTokenRequest(string Token);

/// <summary>
/// The public side of an invitation link (ADR 0014): preview it, or accept it by creating the
/// account. The tenant comes from the invitation the token points to — found through the narrow
/// <c>identity.find_invitation</c> function — never from the request.
/// </summary>
internal sealed class AcceptInvitationHandler(
    IdentityModuleDbContext db,
    TenantContext tenant,
    UserManager<User> users,
    SessionSignIn sessions,
    AuditLog audit,
    TimeProvider time)
{
    public async Task<Result<InvitationPreviewResponse>> PreviewAsync(string token, CancellationToken ct)
    {
        var invitation = await FindUsableAsync(token, ct);
        if (invitation is null)
        {
            return TeamErrors.InvalidInvitation;
        }

        var workspace = await db.Workspaces.SingleAsync(ct);
        var inviter = await db.Users.SingleOrDefaultAsync(u => u.Id == invitation.InvitedBy, ct);
        return Result.Success(new InvitationPreviewResponse(
            workspace.Name,
            inviter?.DisplayName ?? workspace.Name,
            invitation.Email,
            invitation.Role.ToString().ToLowerInvariant(),
            invitation.ExpiresAt));
    }

    public async Task<Result<MeResponse>> AcceptAsync(AcceptInvitationRequest request, CancellationToken ct)
    {
        var invitation = await FindUsableAsync(request.Token, ct);
        if (invitation is null)
        {
            return TeamErrors.InvalidInvitation;
        }

        // Phase 1: one workspace per person (ADR 0014).
        if (await users.FindByEmailAsync(invitation.Email) is not null)
        {
            return TeamErrors.EmailHasAccount;
        }

        var inviter = await db.Users.SingleOrDefaultAsync(u => u.Id == invitation.InvitedBy, ct);
        var user = new User
        {
            UserName = invitation.Email,
            Email = invitation.Email,
            DisplayName = request.Name.Trim(),
            Language = inviter?.Language ?? "en",
            // The link reached this inbox, which proves the address.
            EmailConfirmed = true,
        };

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return created.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName))
                    ? TeamErrors.EmailHasAccount
                    : AuthErrors.ForPassword(created.Errors);
            }

            db.Memberships.Add(Membership.Create(invitation.TenantId, user.Id, invitation.Role));
            invitation.Accept(time.GetUtcNow());
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        await audit.WriteAsync(AuditActions.InvitationAccepted, user.Id, invitation.TenantId, ct);
        return await sessions.SignInAsync(user, remember: false, ct);
    }

    private async Task<Invitation?> FindUsableAsync(string token, CancellationToken ct)
    {
        var hash = InvitationTokens.Hash(token);
        var found = await db.Database
            .SqlQuery<InvitationRow>($"SELECT id, tenant_id FROM identity.find_invitation({hash})")
            .SingleOrDefaultAsync(ct);
        if (found is null)
        {
            return null;
        }

        // From here on the request works inside the invitation's workspace (RLS applies).
        tenant.SetTenant(found.TenantId);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == found.Id, ct);
        return invitation is not null && invitation.IsUsable(time.GetUtcNow()) ? invitation : null;
    }

    private sealed record InvitationRow(Guid Id, Guid TenantId);
}
