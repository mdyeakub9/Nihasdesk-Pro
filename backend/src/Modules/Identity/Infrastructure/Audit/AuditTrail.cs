using Nihasdesk.Modules.Identity.Contracts;
using Nihasdesk.Modules.Identity.Features.Team;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Infrastructure.Audit;

internal sealed class AuditTrail(AuditLog log, CurrentMember current, ITenantContext tenant) : IAuditTrail
{
    public Task RecordAsync(string action, CancellationToken cancellationToken)
    {
        if (action.Length > AuditEvent.ActionMaxLength)
        {
            throw new ArgumentException("Audit action too long.", nameof(action));
        }

        var userId = current.UserId;
        return log.WriteAsync(action, userId == Guid.Empty ? null : userId, tenant.HasTenant ? tenant.TenantId : null, cancellationToken);
    }
}
