using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Infrastructure.Persistence;

/// <summary>
/// Before every save: tenant-owned rows may only be written for the current tenant (CLAUDE.md
/// rule 1), and <see cref="Entity.CreatedAt"/>/<see cref="Entity.UpdatedAt"/> are stamped in UTC.
/// RLS enforces the same rule in the database; this fails earlier with a clear message.
/// </summary>
internal sealed class TenantSaveChangesInterceptor(ITenantContext tenantContext, TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (entry.Entity is TenantEntity tenantOwned
                && (!tenantContext.HasTenant || tenantOwned.TenantId != tenantContext.TenantId))
            {
                throw new InvalidOperationException(
                    $"Refusing to write {entry.Metadata.ClrType.Name} outside the current tenant.");
            }

            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(Entity.CreatedAt)).CurrentValue = now;
            }

            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Property(nameof(Entity.UpdatedAt)).CurrentValue = now;
            }
        }
    }
}
