using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Nihasdesk.SharedKernel;

namespace Nihasdesk.Infrastructure.Persistence;

/// <summary>Implemented by module DbContexts so tenant query filters can read the current tenant.</summary>
public interface ITenantDbContext
{
    /// <summary>Throws when no tenant is set, so a tenant-owned query can never run unscoped.</summary>
    Guid CurrentTenantId { get; }
}

public static class TenantModelBuilderExtensions
{
    public const string TenantFilterName = "tenant";

    /// <summary>
    /// Adds a query filter <c>e.TenantId == context.CurrentTenantId</c> to every entity derived from
    /// <see cref="TenantEntity"/> (first layer of SEC-11; RLS is the second). EF evaluates the context
    /// member per query, so each request sees only its own tenant.
    /// </summary>
    public static ModelBuilder ApplyTenantQueryFilters<TContext>(this ModelBuilder modelBuilder, TContext context)
        where TContext : DbContext, ITenantDbContext
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var tenantTypes = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(TenantEntity).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        foreach (var clrType in tenantTypes)
        {
            var entity = Expression.Parameter(clrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(entity, nameof(TenantEntity.TenantId)),
                    Expression.Property(Expression.Constant(context), nameof(ITenantDbContext.CurrentTenantId))),
                entity);

            modelBuilder.Entity(clrType).HasQueryFilter(TenantFilterName, filter);
        }

        return modelBuilder;
    }
}
