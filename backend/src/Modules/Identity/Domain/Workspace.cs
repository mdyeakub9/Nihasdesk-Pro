using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Identity.Domain;

/// <summary>
/// A shop's workspace. Its ID is the tenant ID for every tenant-owned row (ADR 0003), so
/// <see cref="TenantEntity.TenantId"/> always equals <see cref="Entity.Id"/>.
/// </summary>
internal sealed class Workspace : TenantEntity
{
    public const int NameMaxLength = 200;

    private Workspace(Guid id, Guid organizationId, string name)
        : base(id, id)
    {
        OrganizationId = organizationId;
        Name = name;
    }

    public Guid OrganizationId { get; private set; }

    public string Name { get; private set; }

    public static Workspace Create(Guid organizationId, string name)
    {
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("OrganizationId is required.", nameof(organizationId));
        }

        return new Workspace(Guid.CreateVersion7(), organizationId, WorkspaceNames.Validate(name, NameMaxLength));
    }
}
