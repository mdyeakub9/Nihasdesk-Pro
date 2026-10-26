using Nihasdesk.SharedKernel;

namespace Nihasdesk.Modules.Tests.SharedKernel;

public class EntityTests
{
    [Fact]
    public void New_entities_get_version_7_ids()
    {
        var entity = new SampleEntity(Guid.CreateVersion7());

        Assert.Equal(7, entity.Id.Version);
    }

    [Fact]
    public void Tenant_entity_requires_a_tenant()
    {
        Assert.Throws<ArgumentException>(() => new SampleEntity(Guid.Empty));
    }

    private sealed class SampleEntity(Guid tenantId) : TenantEntity(tenantId);
}
