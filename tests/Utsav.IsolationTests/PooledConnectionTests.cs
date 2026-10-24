using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class PooledConnectionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_connection_reused_by_another_tenant_does_not_retain_the_previous_context()
    {
        // Open, use and release connections as tenant A enough times that the
        // pool is certain to hand the same physical connection back.
        for (var i = 0; i < 8; i++)
        {
            await using var asA = fixture.ContextFor(fixture.TenantA);
            await asA.Zones.IgnoreQueryFilters().ToListAsync();
        }

        await using var asB = fixture.ContextFor(fixture.TenantB);
        var zones = await asB.Zones.IgnoreQueryFilters().ToListAsync();

        // Filters are off, so only the connection's session context stands
        // between B and A's zone. Seeing it would mean the pooled connection
        // still carried A's context.
        Assert.NotEmpty(zones);
        Assert.All(zones, z => Assert.Equal(fixture.TenantB, z.OrgId));
    }

    [Fact]
    public async Task Concurrent_requests_for_different_tenants_never_see_each_others_rows()
    {
        // Interleaved A and B work sharing one pool: the realistic way a
        // stale context would surface.
        var leaks = new System.Collections.Concurrent.ConcurrentBag<string>();

        var work = Enumerable.Range(0, 80).Select(async i =>
        {
            var (tenant, other) = i % 2 == 0
                ? (fixture.TenantA, fixture.TenantB)
                : (fixture.TenantB, fixture.TenantA);

            await using var db = fixture.ContextFor(tenant);
            var rows = await db.Donors.IgnoreQueryFilters().ToListAsync();
            if (rows.Count == 0 || rows.Any(r => r.OrgId == other))
            {
                leaks.Add($"iteration {i}: tenant {tenant} saw {rows.Count} rows, foreign={rows.Count(r => r.OrgId == other)}");
            }
        });

        await Task.WhenAll(work);

        Assert.Empty(leaks);
    }
}
