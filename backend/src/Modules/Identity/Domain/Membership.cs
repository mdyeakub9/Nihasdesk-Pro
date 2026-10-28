using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Domain;

/// <summary>A user's role in one workspace. Users are global; memberships are tenant-owned.</summary>
internal sealed class Membership : TenantEntity
{
    private Membership(Guid tenantId, Guid userId, Role role)
        : base(tenantId)
    {
        UserId = userId;
        Role = role;
    }

    public Guid UserId { get; private set; }

    public Role Role { get; private set; }

    /// <summary>Admin ⇄ Member. The Owner role is fixed (ownership transfer comes later).</summary>
    public void ChangeRole(Role role)
    {
        if (Role == Role.Owner || role == Role.Owner)
        {
            throw new InvalidOperationException("The Owner role can't be given or taken here.");
        }

        Role = role;
    }

    public static Membership Create(Guid tenantId, Guid userId, Role role)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        return new Membership(tenantId, userId, role);
    }
}
