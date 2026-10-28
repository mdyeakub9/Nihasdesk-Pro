namespace Nihasdesk.Modules.Identity.Contracts;

/// <summary>A member of the current workspace, as other modules may see it.</summary>
public sealed record TeamMemberInfo(Guid UserId, string DisplayName, string Role);

/// <summary>
/// Who is on the team of the current workspace (tenant from the request or job). Other modules
/// use this instead of Identity's tables (CLAUDE.md rule 2).
/// </summary>
public interface ITeamDirectory
{
    /// <summary>The signed-in user, or null when they are not (or no longer) a member here.</summary>
    Task<TeamMemberInfo?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>A member of the current workspace, or null for anyone else (also other workspaces' users).</summary>
    Task<TeamMemberInfo?> FindAsync(Guid userId, CancellationToken cancellationToken);
}
