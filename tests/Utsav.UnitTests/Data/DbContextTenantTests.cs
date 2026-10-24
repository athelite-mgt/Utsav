using Microsoft.EntityFrameworkCore;
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Tenancy;
using Xunit;

namespace Utsav.UnitTests.Data;

public class DbContextTenantTests
{
    private static DbContextOptions<UtsavDbContext> Options() =>
        new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    [Fact]
    public void A_context_built_without_a_tenant_refuses_to_supply_one()
    {
        // There is no default. Code that needs a tenant must fail loudly
        // rather than query an arbitrary committee.
        using var db = new UtsavDbContext(Options());

        Assert.False(db.HasTenant);
        Assert.Throws<TenantNotResolvedException>(() => db.TenantId);
    }

    [Fact]
    public void A_context_for_a_request_with_no_resolved_tenant_refuses_to_supply_one()
    {
        // A signed-in user who has not chosen a committee: the request has a
        // TenantContext, but nothing resolved it. Surfaces as 403, never as an
        // empty 200 that reads as "this committee has no data".
        using var db = new UtsavDbContext(Options(), new TenantContext());

        Assert.False(db.HasTenant);
        Assert.Throws<TenantNotResolvedException>(() => db.TenantId);
    }

    [Fact]
    public void A_context_reports_the_requests_resolved_tenant()
    {
        var tenant = new TenantContext();
        var id = Guid.NewGuid();
        tenant.Resolve(id);

        using var db = new UtsavDbContext(Options(), tenant);

        Assert.True(db.HasTenant);
        Assert.Equal(id, db.TenantId);
    }
}
