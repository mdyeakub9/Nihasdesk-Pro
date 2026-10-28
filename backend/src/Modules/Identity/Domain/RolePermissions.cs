namespace Nihasdesk.Modules.Identity.Domain;

/// <summary>Team permissions (docs/standards/permissions.md). Other modules add theirs with their features.</summary>
internal enum Permission
{
    TeamView,
    TeamInvite,
    TeamInviteAdmin,
    TeamChangeRole,
    TeamRemoveMember,
    TeamRemoveAdmin,
}

/// <summary>The permission matrix. Change together with docs/standards/permissions.md and the per-role tests.</summary>
internal static class RolePermissions
{
    private static readonly Dictionary<Role, HashSet<Permission>> Matrix = new()
    {
        [Role.Owner] =
        [
            Permission.TeamView, Permission.TeamInvite, Permission.TeamInviteAdmin, Permission.TeamChangeRole,
            Permission.TeamRemoveMember, Permission.TeamRemoveAdmin,
        ],
        [Role.Admin] = [Permission.TeamView, Permission.TeamInvite, Permission.TeamRemoveMember],
        [Role.Member] = [Permission.TeamView],
    };

    public static bool Has(Role role, Permission permission) => Matrix[role].Contains(permission);
}
