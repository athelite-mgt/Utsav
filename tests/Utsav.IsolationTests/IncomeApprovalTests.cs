using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Collections recorded by anyone but Finance/Admin wait for approval. These
/// pin what a member can and cannot do to a bill, and that approved money is
/// never disturbed by a re-save. Run in committee C.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class IncomeApprovalTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<HttpClient> RoleClientAsync(string role)
    {
        var (_, token) = await fixture.NewMemberAsync($"income-{role}", [(fixture.TenantC, role, "active")], fixture.TenantC);
        return fixture.ClientFor(token);
    }

    /// <summary>A fresh ₹1000 bill for a fresh donor, made by the committee admin.</summary>
    private async Task<Guid> NewBillAsync()
    {
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var donor = await admin.PostAsJsonAsync("/api/donors", new { name = "Income " + Guid.NewGuid().ToString("N")[..6] });
        Assert.True(donor.StatusCode == HttpStatusCode.OK, await donor.Content.ReadAsStringAsync());
        var donorId = (await JsonAsync(donor)).GetProperty("id").GetGuid();

        var bill = await admin.PostAsJsonAsync("/api/subscriptions", new
        {
            donor_id = donorId, year_id = fixture.C.Year, category = "membership", billed = 1000, collected = 0,
        });
        Assert.True(bill.StatusCode == HttpStatusCode.OK, await bill.Content.ReadAsStringAsync());
        return (await JsonAsync(bill)).GetProperty("id").GetGuid();
    }

    private static object Payment(decimal amount, string mode = "Cash") => new { amount, paid_on = "2026-10-20", mode };

    private async Task<List<(decimal Amount, string Status)>> InstalmentsAsync(Guid bill)
    {
        await using var db = fixture.ContextFor(fixture.TenantC);
        return (await db.CollectionPayments.Where(p => p.ParentId == bill).OrderBy(p => p.CreatedAt).ToListAsync())
            .Select(p => (p.Amount, p.Status)).ToList();
    }

    private async Task<decimal> CollectedAsync(Guid bill)
    {
        await using var db = fixture.ContextFor(fixture.TenantC);
        return (await db.Subscriptions.SingleAsync(s => s.Id == bill)).Collected ?? 0m;
    }

    [Fact]
    public async Task An_executives_payment_is_pending_and_counts_on_the_bill()
    {
        var bill = await NewBillAsync();
        using var executive = await RoleClientAsync("executive");

        var response = await executive.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, collected = 500, payments = new[] { Payment(500) },
        });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal([(500m, "pending")], await InstalmentsAsync(bill));
        Assert.Equal(500m, await CollectedAsync(bill));
    }

    [Fact]
    public async Task A_finance_payment_goes_straight_through()
    {
        var bill = await NewBillAsync();
        using var finance = await RoleClientAsync("finance");

        var response = await finance.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, collected = 300, payments = new[] { Payment(300) },
        });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal([(300m, "approved")], await InstalmentsAsync(bill));
    }

    [Fact]
    public async Task Resaving_a_bill_never_disturbs_approved_money()
    {
        var bill = await NewBillAsync();
        using var finance = await RoleClientAsync("finance");
        using var executive = await RoleClientAsync("executive");
        await finance.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, collected = 300, payments = new[] { Payment(300) },
        });

        // The executive re-sends the approved instalment plus a new one.
        await executive.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, payments = new[] { Payment(300), Payment(200) },
        });
        var afterAdd = await InstalmentsAsync(bill);
        Assert.Contains((300m, "approved"), afterAdd);
        Assert.Contains((200m, "pending"), afterAdd);

        // Then sends a list that omits the approved one: it stays, the own pending one goes.
        await executive.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, payments = Array.Empty<object>(),
        });
        Assert.Equal([(300m, "approved")], await InstalmentsAsync(bill));
        Assert.Equal(300m, await CollectedAsync(bill));
    }

    [Fact]
    public async Task An_executive_cannot_create_or_delete_a_bill()
    {
        var bill = await NewBillAsync();
        using var executive = await RoleClientAsync("executive");

        var create = await executive.PostAsJsonAsync("/api/subscriptions", new
        {
            donor_id = fixture.C.Donor, year_id = fixture.C.Year, category = "membership", billed = 5,
        });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await executive.DeleteAsync($"/api/subscriptions/{bill}")).StatusCode);
    }

    [Fact]
    public async Task An_executive_cannot_change_what_the_donor_was_asked_for()
    {
        var bill = await NewBillAsync();
        using var executive = await RoleClientAsync("executive");

        await executive.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, billed = 1, bill_no = "HACK", comments = "paid in person",
            payments = new[] { Payment(100) },
        });

        await using var db = fixture.ContextFor(fixture.TenantC);
        var row = await db.Subscriptions.SingleAsync(s => s.Id == bill);
        Assert.Equal(1000m, row.Billed);
        Assert.Null(row.BillNo);
        Assert.Equal("paid in person", row.Comments);
    }

    [Fact]
    public async Task A_secretariat_member_cannot_overwrite_the_collected_total_directly()
    {
        var bill = await NewBillAsync();
        using var secretariat = await RoleClientAsync("secretariat");

        await secretariat.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, collected = 9999, payments = new[] { Payment(150) },
        });

        Assert.Equal(150m, await CollectedAsync(bill));
        Assert.Equal([(150m, "pending")], await InstalmentsAsync(bill));
    }

    [Fact]
    public async Task A_viewer_cannot_record_a_payment()
    {
        var bill = await NewBillAsync();
        using var viewer = await RoleClientAsync("viewer");

        var response = await viewer.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, payments = new[] { Payment(100) },
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await InstalmentsAsync(bill));
    }
}
