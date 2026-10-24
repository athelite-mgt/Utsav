using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// The three reporting views aggregate across tables. Their inner subqueries
/// group by year_id and category without naming org_id, which is safe only
/// because year_id is itself unique to a committee. These tests prove that
/// reasoning with numbers rather than trusting it.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class ViewIsolationTests(DatabaseFixture fixture)
{
    // Seeded for BOTH committees: a Decoration expense (A 1000, B 2000) and a
    // 9000 Decoration contract with nothing paid. The view counts an unpaid
    // contract as committed spend, so actual = expense + 9000.
    private const decimal ExpectedA = 1000m + 9000m;
    private const decimal ExpectedB = 2000m + 9000m;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Budget_vs_actual_does_not_aggregate_across_committees(bool withEfFilters)
    {
        // With the EF filter off, only row-level security stands between the
        // view and the other committee's expenses.
        await using var asA = fixture.ContextFor(fixture.TenantA);
        await using var asB = fixture.ContextFor(fixture.TenantB);

        var rowsA = await (withEfFilters ? asA.VBudgetVsActuals : asA.VBudgetVsActuals.IgnoreQueryFilters()).ToListAsync();
        var rowsB = await (withEfFilters ? asB.VBudgetVsActuals : asB.VBudgetVsActuals.IgnoreQueryFilters()).ToListAsync();

        Assert.NotEmpty(rowsA);
        Assert.NotEmpty(rowsB);
        Assert.All(rowsA, r => Assert.Equal(fixture.TenantA, r.OrgId));
        Assert.All(rowsB, r => Assert.Equal(fixture.TenantB, r.OrgId));

        // 21000 (= both committees' figures summed) here would mean the
        // expense subquery merged them.
        Assert.Equal(ExpectedA, rowsA.Single(r => r.Category == "Decoration").Actual);
        Assert.Equal(ExpectedB, rowsB.Single(r => r.Category == "Decoration").Actual);
    }

    [Fact]
    public async Task Contract_status_view_is_scoped_and_computes_from_its_own_committee()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);
        await using var asB = fixture.ContextFor(fixture.TenantB);

        var a = await asA.VContractStatuses.IgnoreQueryFilters().ToListAsync();
        var b = await asB.VContractStatuses.IgnoreQueryFilters().ToListAsync();

        Assert.Single(a);
        Assert.Single(b);
        Assert.Equal(fixture.A.Contract, a[0].Id);
        Assert.Equal(fixture.B.Contract, b[0].Id);
        Assert.Contains(fixture.A.Marker, a[0].Title);
        Assert.Contains(fixture.B.Marker, b[0].Title);
        Assert.Contains(fixture.A.Marker, a[0].VendorName);
    }

    [Fact]
    public async Task Advance_balances_view_is_scoped_and_computes_from_its_own_committee()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);
        await using var asB = fixture.ContextFor(fixture.TenantB);

        var a = await asA.VAdvanceBalances.IgnoreQueryFilters().ToListAsync();
        var b = await asB.VAdvanceBalances.IgnoreQueryFilters().ToListAsync();

        Assert.Single(a);
        Assert.Single(b);
        Assert.Equal(1500m, a[0].Outstanding);
        Assert.Equal(2500m, b[0].Outstanding);
    }

    [Fact]
    public async Task A_context_with_no_tenant_reads_nothing_from_any_view()
    {
        await using var nobody = fixture.ContextWithNoTenant();

        Assert.Empty(await nobody.VBudgetVsActuals.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await nobody.VContractStatuses.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await nobody.VAdvanceBalances.IgnoreQueryFilters().ToListAsync());
    }
}
