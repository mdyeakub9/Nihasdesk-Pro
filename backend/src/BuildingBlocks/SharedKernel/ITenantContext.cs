namespace Nihasdesk.SharedKernel;

/// <summary>
/// The current tenant, resolved only from the authenticated context (CLAUDE.md rule 1).
/// Implemented by the Identity module; used by query filters and RLS (SEC-11).
/// </summary>
public interface ITenantContext
{
    bool HasTenant { get; }

    /// <summary>Throws when no tenant is resolved, so tenant-owned code can never run unscoped.</summary>
    Guid TenantId { get; }
}
