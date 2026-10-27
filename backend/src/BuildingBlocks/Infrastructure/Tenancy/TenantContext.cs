using Nihasdesk.SharedKernel;

namespace Nihasdesk.Infrastructure.Tenancy;

/// <summary>
/// Scoped holder of the current tenant. Set once per request (or job) from the authenticated
/// context, never from request bodies or AI output (CLAUDE.md rule 1). Setting it twice throws,
/// so a scope can never switch tenants halfway.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId =>
        _tenantId ?? throw new InvalidOperationException("No tenant is set for this scope.");

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        if (_tenantId.HasValue && _tenantId != tenantId)
        {
            throw new InvalidOperationException("The tenant for this scope is already set.");
        }

        _tenantId = tenantId;
    }
}
