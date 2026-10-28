using Microsoft.EntityFrameworkCore;
using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Identity.Infrastructure;

namespace Nihasdesk.Modules.Identity.Features.Team;

/// <summary>Reads memberships through the tenant-scoped context, so only this workspace's members are found.</summary>
internal sealed class TeamDirectory(IdentityModuleDbContext db, CurrentMember current) : ITeamDirectory
{
    private TeamMemberInfo? _current;

    public async Task<TeamMemberInfo?> CurrentAsync(CancellationToken cancellationToken)
    {
        var userId = current.UserId;
        if (userId == Guid.Empty)
        {
            return null;
        }

        // Once per request: the endpoint filter and the handler both ask.
        return _current ??= await FindAsync(userId, cancellationToken);
    }

    public async Task<TeamMemberInfo?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await db.Memberships
            .Where(m => m.UserId == userId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.DisplayName, m.Role })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new TeamMemberInfo(row.Id, row.DisplayName, row.Role.ToString().ToLowerInvariant());
    }
}
