using Utsav.Api.Endpoints;

namespace Utsav.UnitTests.Audit;

/// <summary>
/// Pins the reported behavior: Membership and Special Donation annexures on
/// the Audit Report should only list donors actually billed this year — a
/// donor with billed 0 (or never set, which the query maps to 0 via
/// AuditReportEndpoint's Dec() helper) has nothing for a CA to trace and
/// clutters the annexure. <see cref="AuditReportEndpoint.FilterBilled"/> is
/// the pure function both IncomeAnnexureAsync calls (membership, special)
/// route through — Ads and Other Incomes annexures don't call it, since the
/// user only asked for "members/special members".
/// </summary>
public class AuditReportEndpointTests
{
    private static AuditReportEndpoint.IncomeAnnexureRow Row(string name, decimal billed) => new(
        Name: name, Zone: "North Zone", BillNo: "B-1", Billed: billed, BilledOn: "2026-09-01",
        Collected: 0, CollectedOn: null, Due: billed, Mode: "Cash");

    [Fact]
    public void Drops_rows_with_zero_billed()
    {
        var rows = new[] { Row("Zero Billed", 0m), Row("Real Donor", 5000m) };

        var filtered = AuditReportEndpoint.FilterBilled(rows);

        var kept = Assert.Single(filtered);
        Assert.Equal("Real Donor", kept.Name);
    }

    [Fact]
    public void Keeps_rows_with_a_nonzero_billed_amount()
    {
        var rows = new[] { Row("Donor A", 1000m), Row("Donor B", 2500m) };

        var filtered = AuditReportEndpoint.FilterBilled(rows);

        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void Empty_input_yields_empty_output()
    {
        Assert.Empty(AuditReportEndpoint.FilterBilled([]));
    }
}
