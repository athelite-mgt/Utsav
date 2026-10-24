using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class WriteIsolationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_created_row_is_stamped_with_the_callers_committee_not_the_bodys()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        // org_id in the body is never accepted: TableRegistry does not list the
        // column, so it is dropped before the statement is built.
        var response = await asC.PostAsJsonAsync("/api/vendors", new { name = "Forged Vendor", org_id = fixture.TenantB });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var inC = fixture.ContextFor(fixture.TenantC);
        await using var inB = fixture.ContextFor(fixture.TenantB);
        Assert.Equal(fixture.TenantC, (await inC.Vendors.SingleAsync(v => v.Name == "Forged Vendor")).OrgId);
        Assert.False(await inB.Vendors.AnyAsync(v => v.Name == "Forged Vendor"));
    }

    [Theory]
    [InlineData("donors", "name")]
    [InlineData("vendors", "name")]
    [InlineData("zones", "name")]
    [InlineData("collectors", "name")]
    public async Task Updating_another_committees_row_by_id_changes_nothing(string table, string column)
    {
        var id = table switch
        {
            "donors" => fixture.B.Donor,
            "vendors" => fixture.B.Vendor,
            "zones" => fixture.B.Zone,
            _ => fixture.B.Collector,
        };

        using var asA = fixture.ClientFor(fixture.TokenForA);
        var response = await asA.PatchAsJsonAsync($"/api/{table}/{id}", new Dictionary<string, object> { [column] = "HIJACKED" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var inB = fixture.ContextFor(fixture.TenantB);
        var stillTheirs = table switch
        {
            "donors" => (await inB.Donors.SingleAsync(d => d.Id == id)).Name,
            "vendors" => (await inB.Vendors.SingleAsync(v => v.Id == id)).Name,
            "zones" => (await inB.Zones.SingleAsync(z => z.Id == id)).Name,
            _ => (await inB.Collectors.SingleAsync(c => c.Id == id)).Name,
        };
        Assert.DoesNotContain("HIJACKED", stillTheirs);
        Assert.Contains(fixture.B.Marker, stillTheirs);
    }

    [Theory]
    [InlineData("zones")]
    [InlineData("expenses")]
    [InlineData("vendors")]
    [InlineData("cash_ledger")]
    public async Task Deleting_another_committees_row_by_id_deletes_nothing(string table)
    {
        var id = table switch
        {
            "zones" => fixture.B.Zone,
            "expenses" => fixture.B.Expense,
            "vendors" => fixture.B.Vendor,
            _ => fixture.B.CashEntry,
        };

        using var asA = fixture.ClientFor(fixture.TokenForA);
        var response = await asA.DeleteAsync($"/api/{table}/{id}");

        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var inB = fixture.ContextFor(fixture.TenantB);
        var survived = table switch
        {
            "zones" => await inB.Zones.AnyAsync(z => z.Id == id),
            "expenses" => await inB.Expenses.AnyAsync(e => e.Id == id),
            "vendors" => await inB.Vendors.AnyAsync(v => v.Id == id),
            _ => await inB.CashLedgers.AnyAsync(c => c.Id == id),
        };
        Assert.True(survived, $"A deleted B's {table} row through the API");
    }

    [Fact]
    public async Task A_record_cannot_reference_another_committees_vendor()
    {
        // SQL Server foreign-key checks ignore row-level security, so the
        // database would happily link A's expense to B's vendor. The ids are
        // unguessable, but a cross-committee link is never legitimate and would
        // block B from ever deleting that vendor.
        using var asA = fixture.ClientFor(fixture.TokenForA);

        var response = await asA.PostAsJsonAsync("/api/expenses", new
        {
            year_id = fixture.A.Year, category = "Decoration", amount = 10, details = "cross-ref probe",
            vendor_id = fixture.B.Vendor,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var inA = fixture.ContextFor(fixture.TenantA);
        Assert.False(await inA.Expenses.AnyAsync(e => e.Details == "cross-ref probe"));
    }

    [Fact]
    public async Task A_record_cannot_be_filed_under_another_committees_puja_year()
    {
        using var asA = fixture.ClientFor(fixture.TokenForA);

        var response = await asA.PostAsJsonAsync("/api/cash_ledger", new
        {
            year_id = fixture.B.Year, party = "year probe", amount = 5, entry_date = "2026-10-05",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var inA = fixture.ContextFor(fixture.TenantA);
        Assert.False(await inA.CashLedgers.AnyAsync(c => c.Party == "year probe"));
    }

    [Fact]
    public async Task A_legitimate_reference_within_the_same_committee_still_works()
    {
        // The guard above must not turn into a blanket refusal of foreign keys.
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var response = await asC.PostAsJsonAsync("/api/expenses", new
        {
            year_id = fixture.C.Year, category = "Decoration", amount = 11, details = "own-ref ok",
            vendor_id = fixture.C.Vendor,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_write_is_audited_under_the_callers_committee()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        var tag = $"audit-{Guid.NewGuid():N}";

        var response = await asC.PostAsJsonAsync("/api/cash_ledger", new
        {
            year_id = fixture.C.Year, party = tag, amount = 7, entry_date = "2026-10-06",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The Cosmos container is partitioned on /orgId: an event filed under
        // the wrong committee would put one committee's trail in another's.
        var entries = fixture.Audit.Entries
            .Where(e => e.After is not null && e.After.Values.Any(v => Equals(v, tag)))
            .ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal(fixture.TenantC, e.OrgId));
    }

    [Fact]
    public async Task A_transaction_notification_is_recorded_for_the_committee_that_made_it()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        var tag = $"notify-{Guid.NewGuid():N}";

        var response = await asC.PostAsJsonAsync("/api/expenses", new
        {
            year_id = fixture.C.Year, category = "Decoration", amount = 12, details = tag,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The push worker runs in the background, outside any request, so its
        // tenant has to be established from the notification itself.
        var seenByC = 0;
        for (var attempt = 0; attempt < 40 && seenByC == 0; attempt++)
        {
            await using var inC = fixture.ContextFor(fixture.TenantC);
            seenByC = await inC.Notifications.CountAsync();
            if (seenByC == 0)
            {
                await Task.Delay(100);
            }
        }

        await using var inA = fixture.ContextFor(fixture.TenantA);
        await using var inB = fixture.ContextFor(fixture.TenantB);
        Assert.True(seenByC > 0, "the notification was never recorded for the committee that made the change");
        Assert.Equal(0, await inA.Notifications.CountAsync());
        Assert.Equal(0, await inB.Notifications.CountAsync());
    }
}
