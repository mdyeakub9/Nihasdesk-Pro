using Microsoft.EntityFrameworkCore;
using Nihasdesk.Infrastructure.Email.Outbox;
using Nihasdesk.Infrastructure.Tenancy;
using Nihasdesk.Modules.Identity.Domain;
using Nihasdesk.Modules.Identity.Infrastructure;
using Nihasdesk.Modules.Identity.Infrastructure.Audit;
using Nihasdesk.Modules.Identity.Infrastructure.Email;
using Nihasdesk.Modules.Identity.Infrastructure.Invitations;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Features.Team;

/// <summary>
/// Team management inside the current workspace. Every query goes through the tenant-scoped
/// context, so IDs of another workspace are simply not found (SEC-06). Permissions are checked
/// by <see cref="RequirePermission"/> on the endpoint plus the role rules here.
/// </summary>
internal sealed class TeamHandler(
    IdentityModuleDbContext db,
    TenantContext tenant,
    CurrentMember current,
    IEmailOutbox email,
    DashboardLinks links,
    AuditLog audit,
    TimeProvider time)
{
    public async Task<IReadOnlyList<MemberResponse>> MembersAsync(CancellationToken ct)
    {
        var rows = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.Role, m.CreatedAt, u.Id, u.DisplayName, u.Email })
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(x => new MemberResponse(x.Id, x.DisplayName, x.Email!, x.Role.ToString().ToLowerInvariant(), x.CreatedAt)).ToList();
    }

    public async Task<IReadOnlyList<InvitationResponse>> InvitationsAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var pending = await db.Invitations
            .Where(i => i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(ct);
        return pending.Select(ToResponse).ToList();
    }

    public async Task<Result<InvitationResponse>> InviteAsync(InviteRequest request, CancellationToken ct)
    {
        var role = AssignableRoles.Parse(request.Role);
        var inviter = (await current.GetAsync(ct))!;
        if (role == Role.Admin && !RolePermissions.Has(inviter.Role, Permission.TeamInviteAdmin))
        {
            return TeamErrors.Forbidden;
        }

        var address = Invitation.NormalizeEmail(request.Email);
        // Identity stores NormalizedEmail upper-case; compare with the same normalization.
        var normalized = address.ToUpperInvariant();
        var alreadyMember = await db.Memberships
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.NormalizedEmail)
            .AnyAsync(e => e == normalized, ct);
        if (alreadyMember)
        {
            return TeamErrors.AlreadyMember;
        }

        // One open invitation per email: a new one replaces the old link.
        var now = time.GetUtcNow();
        foreach (var open in await db.Invitations.Where(i => i.Email == address && i.AcceptedAt == null && i.RevokedAt == null).ToListAsync(ct))
        {
            open.Revoke(now);
        }

        var (token, hash) = InvitationTokens.Create();
        var invitation = Invitation.Create(tenant.TenantId, address, role, hash, inviter.UserId, now);
        db.Invitations.Add(invitation);
        await SendAsync(invitation, token, ct);
        await audit.WriteAsync(AuditActions.InvitationCreated, inviter.UserId, tenant.TenantId, ct);
        return Result.Success(ToResponse(invitation));
    }

    public async Task<Result<InvitationResponse>> ResendAsync(Guid invitationId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var old = await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId, ct);
        if (old is null || !old.IsUsable(now))
        {
            return TeamErrors.NotFound;
        }

        old.Revoke(now);
        var (token, hash) = InvitationTokens.Create();
        var invitation = Invitation.Create(tenant.TenantId, old.Email, old.Role, hash, current.UserId, now);
        db.Invitations.Add(invitation);
        await SendAsync(invitation, token, ct);
        await audit.WriteAsync(AuditActions.InvitationResent, current.UserId, tenant.TenantId, ct);
        return Result.Success(ToResponse(invitation));
    }

    public async Task<Result> RevokeAsync(Guid invitationId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId, ct);
        if (invitation is null || !invitation.IsUsable(now))
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        invitation.Revoke(now);
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.InvitationRevoked, current.UserId, tenant.TenantId, ct);
        return Result.Success();
    }

    public async Task<Result> ChangeRoleAsync(Guid userId, ChangeRoleRequest request, CancellationToken ct)
    {
        var target = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, ct);
        if (target is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        if (target.Role == Role.Owner)
        {
            return Result.Failure(TeamErrors.OwnerIsFixed);
        }

        target.ChangeRole(AssignableRoles.Parse(request.Role));
        await db.SaveChangesAsync(ct);
        await audit.WriteAsync(AuditActions.MemberRoleChanged, current.UserId, tenant.TenantId, ct);
        return Result.Success();
    }

    public async Task<Result> RemoveAsync(Guid userId, CancellationToken ct)
    {
        var target = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, ct);
        if (target is null)
        {
            return Result.Failure(TeamErrors.NotFound);
        }

        if (target.Role == Role.Owner)
        {
            return Result.Failure(TeamErrors.OwnerIsFixed);
        }

        var remover = (await current.GetAsync(ct))!;
        var needed = target.Role == Role.Admin ? Permission.TeamRemoveAdmin : Permission.TeamRemoveMember;
        if (!RolePermissions.Has(remover.Role, needed))
        {
            return Result.Failure(TeamErrors.Forbidden);
        }

        db.Memberships.Remove(target);
        await db.SaveChangesAsync(ct);
        // Signed out of this workspace everywhere, immediately.
        var tenantId = tenant.TenantId;
        await db.Sessions.Where(s => s.UserId == userId && s.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await audit.WriteAsync(AuditActions.MemberRemoved, current.UserId, tenantId, ct);
        return Result.Success();
    }

    /// <summary>Queues the email in the same save as the invitation (outbox, ADR 0013).</summary>
    private async Task SendAsync(Invitation invitation, string token, CancellationToken ct)
    {
        var workspace = await db.Workspaces.SingleAsync(ct);
        var inviter = await db.Users.SingleAsync(u => u.Id == current.UserId, ct);
        await email.EnqueueAsync(
            AuthEmails.Invitation(invitation.Email, inviter.Language, workspace.Name, inviter.DisplayName, links.AcceptInvitation(token)),
            ct);
    }

    private static InvitationResponse ToResponse(Invitation i) =>
        new(i.Id, i.Email, i.Role.ToString().ToLowerInvariant(), i.CreatedAt, i.ExpiresAt);
}
