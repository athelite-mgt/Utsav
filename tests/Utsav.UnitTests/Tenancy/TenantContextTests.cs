using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Tenancy;
using Xunit;

namespace Utsav.UnitTests.Tenancy;

public class TenantContextTests
{
    [Fact]
    public void TenantId_throws_when_unresolved()
    {
        var context = new TenantContext();

        Assert.False(context.HasTenant);
        // Fail closed: there is no default tenant, and asking for one that
        // was never established is a bug, not an empty result.
        Assert.Throws<TenantNotResolvedException>(() => context.TenantId);
    }

    [Fact]
    public void Resolve_sets_the_tenant_once()
    {
        var tenant = Guid.NewGuid();
        var context = new TenantContext();

        context.Resolve(tenant);

        Assert.True(context.HasTenant);
        Assert.Equal(tenant, context.TenantId);
    }

    [Fact]
    public void Resolve_refuses_to_change_an_established_tenant()
    {
        var context = new TenantContext();
        context.Resolve(Guid.NewGuid());

        // A request's tenant must be fixed for its whole lifetime. Allowing a
        // second Resolve would make mid-request tenant switching expressible,
        // which is exactly the bug class this type exists to prevent.
        Assert.Throws<InvalidOperationException>(() => context.Resolve(Guid.NewGuid()));
    }

    [Fact]
    public void Resolve_refuses_an_empty_tenant_id()
    {
        var context = new TenantContext();

        Assert.Throws<ArgumentException>(() => context.Resolve(Guid.Empty));
    }
}
