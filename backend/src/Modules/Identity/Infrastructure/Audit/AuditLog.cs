using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Nihasdesk.Modules.Identity.Infrastructure.Audit;

/// <summary>
/// Writes audit events in their own scope and connection, so they are kept even when the
/// request's own transaction rolls back, and never disturb its change tracker.
/// </summary>
internal sealed class AuditLog(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContext, TimeProvider timeProvider)
{
    public async Task WriteAsync(string action, Guid? userId, Guid? tenantId, CancellationToken cancellationToken)
    {
        var ip = httpContext.HttpContext?.Connection.RemoteIpAddress?.ToString();

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.AuditEvents.Add(new AuditEvent
        {
            OccurredAt = timeProvider.GetUtcNow(),
            Action = action,
            UserId = userId,
            TenantId = tenantId,
            IpAddress = ip is { Length: <= AuditEvent.IpAddressMaxLength } ? ip : null,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
