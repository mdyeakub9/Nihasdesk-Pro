namespace Nihasdesk.SharedKernel;

/// <summary>Base for all entities. IDs are Guid v7 (time-ordered); timestamps are UTC.</summary>
public abstract class Entity
{
    protected Entity()
        : this(Guid.CreateVersion7())
    {
    }

    protected Entity(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; private init; }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }
}

/// <summary>
/// Base for tenant-owned entities. <see cref="TenantId"/> is set once from the authenticated
/// tenant context and never from request bodies or AI output (CLAUDE.md rule 1, SEC-22).
/// </summary>
public abstract class TenantEntity : Entity
{
    protected TenantEntity(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        }

        TenantId = tenantId;
    }

    public Guid TenantId { get; private init; }
}
