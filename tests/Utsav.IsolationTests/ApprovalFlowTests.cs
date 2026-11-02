using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Finance's desk: approve (optionally for less), send back, reject, bulk-approve
/// a member's handover, and refund own-pocket claims. Run in committee C.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class ApprovalFlowTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(Guid UserId, HttpClient Client)> RoleClientAsync(string role, string? label = null)
    {
        var (userId, token) = await fixture.NewMemberAsync(label ?? $"flow-{role}", [(fixture.TenantC, role, "active")], fixture.TenantC);
        return (userId, fixture.ClientFor(token));
    }

    private async Task<Guid> NewBillAsync(decimal billed = 1000)
    {
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var donor = await admin.PostAsJsonAsync("/api/donors", new { name = "Flow " + Guid.NewGuid().ToString("N")[..6] });
        var donorId = (await JsonAsync(donor)).GetProperty("id").GetGuid();
        var bill = await admin.PostAsJsonAsync("/api/subscriptions", new
        {
            donor_id = donorId, year_id = fixture.C.Year, category = "membership", billed, collected = 0,
        });
        return (await JsonAsync(bill)).GetProperty("id").GetGuid();
    }

    /// <summary>An executive-style pending instalment on a bill; returns its id.</summary>
    private async Task<Guid> PendingInstalmentAsync(HttpClient member, Guid bill, decimal amount)
    {
        var response = await member.PatchAsJsonAsync($"/api/subscriptions/{bill}", new
        {
            year_id = fixture.C.Year, payments = new[] { new { amount, paid_on = "2026-10-20", mode = "Cash" } },
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        await using var db = fixture.ContextFor(fixture.TenantC);
        return (await db.CollectionPayments.Where(p => p.ParentId == bill && p.Status == "pending").OrderByDescending(p => p.CreatedAt).FirstAsync()).Id;
    }

    private async Task<Guid> PendingExpenseAsync(HttpClient member, decimal amount, string paidFrom = "fund")
    {
        var response = await member.PostAsJsonAsync("/api/expenses", new
        {
            year_id = fixture.C.Year, category = "Decoration", details = "Flow claim", amount, spent_on = "2026-10-20", mode = "Cash", paid_from = paidFrom,
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<List<(decimal Amount, string Status)>> InstalmentsAsync(Guid bill)
    {
        await using var db = fixture.ContextFor(fixture.TenantC);
        return (await db.CollectionPayments.Where(p => p.ParentId == bill).OrderBy(p => p.CreatedAt).ToListAsync())
            .Select(p => (p.Amount, p.Status)).ToList();
    }

    [Fact]
    public async Task Approving_moves_an_instalment_to_approved_and_records_who_decided()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (financeId, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var bill = await NewBillAsync();
        var item = await PendingInstalmentAsync(executive, bill, 500);

        var response = await finance.PostAsJsonAsync($"/api/approvals/income/{item}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.ContextFor(fixture.TenantC);
        var row = await db.CollectionPayments.SingleAsync(p => p.Id == item);
        Assert.Equal("approved", row.Status);
        Assert.Equal(financeId, row.DecidedByUserId);
        Assert.NotNull(row.DecidedAt);

        // The bill still shows the payment, and none of it is pending any more.
        var parent = await db.Subscriptions.SingleAsync(s => s.Id == bill);
        Assert.Equal(500m, parent.Collected);
        Assert.Equal(0m, parent.PendingCollected);
    }

    [Fact]
    public async Task Approving_for_less_approves_that_amount_and_leaves_the_rest_pending_with_the_member()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var bill = await NewBillAsync();
        var item = await PendingInstalmentAsync(executive, bill, 500);

        var response = await finance.PostAsJsonAsync($"/api/approvals/income/{item}/approve", new { amount = 400 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await InstalmentsAsync(bill);
        Assert.Contains((400m, "approved"), rows);
        Assert.Contains((100m, "pending"), rows);
        await using var db = fixture.ContextFor(fixture.TenantC);
        var parent = await db.Subscriptions.SingleAsync(s => s.Id == bill);
        Assert.Equal(500m, parent.Collected);
        Assert.Equal(100m, parent.PendingCollected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(501)]
    public async Task An_approved_amount_must_be_positive_and_not_more_than_was_recorded(decimal amount)
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var item = await PendingInstalmentAsync(executive, await NewBillAsync(), 500);

        var response = await finance.PostAsJsonAsync($"/api/approvals/income/{item}/approve", new { amount });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Sending_back_needs_a_note_and_a_sent_back_item_cannot_be_approved_until_resubmitted()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var item = await PendingInstalmentAsync(executive, await NewBillAsync(), 300);

        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/approvals/income/{item}/send_back", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync($"/api/approvals/income/{item}/send_back", new { note = "Amount looks wrong" })).StatusCode);

        await using var db = fixture.ContextFor(fixture.TenantC);
        var row = await db.CollectionPayments.SingleAsync(p => p.Id == item);
        Assert.Equal("sent_back", row.Status);
        Assert.Equal("Amount looks wrong", row.DecisionNote);
        Assert.Equal(HttpStatusCode.Conflict, (await finance.PostAsJsonAsync($"/api/approvals/income/{item}/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_puts_the_bill_back_to_unpaid()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var bill = await NewBillAsync(1000);
        var item = await PendingInstalmentAsync(executive, bill, 500);

        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/approvals/income/{item}/reject", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync($"/api/approvals/income/{item}/reject", new { reason = "Never received" })).StatusCode);

        await using var db = fixture.ContextFor(fixture.TenantC);
        var row = await db.Subscriptions.SingleAsync(s => s.Id == bill);
        Assert.Equal(0m, row.Collected);
        Assert.Equal(0m, row.PendingCollected);
        Assert.Equal(1000m, row.Due);
    }

    [Fact]
    public async Task Bulk_approving_a_member_approves_all_their_pending_items_and_nobody_elses()
    {
        var (_, rakesh) = await RoleClientAsync("executive", "Rakesh Bulk");
        var (_, other) = await RoleClientAsync("executive", "Other Bulk");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = rakesh; using var _2 = other; using var _3 = finance;

        var bill1 = await NewBillAsync();
        var bill2 = await NewBillAsync();
        await PendingInstalmentAsync(rakesh, bill1, 200);
        await PendingInstalmentAsync(rakesh, bill2, 300);
        var expense = await PendingExpenseAsync(rakesh, 50);
        var otherItem = await PendingInstalmentAsync(other, await NewBillAsync(), 70);

        var response = await finance.PostAsJsonAsync("/api/approvals/approve_member", new { name = "Rakesh Bulk" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(3, body.GetProperty("approved").GetInt32());
        Assert.Equal(550m, body.GetProperty("total").GetDecimal());
        Assert.All(await InstalmentsAsync(bill1), r => Assert.Equal("approved", r.Status));
        await using var db = fixture.ContextFor(fixture.TenantC);
        Assert.Equal("approved", (await db.Expenses.SingleAsync(e => e.Id == expense)).Status);
        Assert.Equal("pending", (await db.CollectionPayments.SingleAsync(p => p.Id == otherItem)).Status);
    }

    [Theory]
    [InlineData("executive")]
    [InlineData("secretariat")]
    [InlineData("viewer")]
    public async Task Only_finance_and_admin_can_decide(string role)
    {
        var (_, member) = await RoleClientAsync(role);
        var (_, executive) = await RoleClientAsync("executive");
        using var _1 = member; using var _2 = executive;
        var item = await PendingInstalmentAsync(executive, await NewBillAsync(), 100);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/approvals/income/{item}/approve", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/approvals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/approvals/approve_member", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task Admin_can_approve_too()
    {
        var (_, executive) = await RoleClientAsync("executive");
        using var _1 = executive;
        var item = await PendingInstalmentAsync(executive, await NewBillAsync(), 100);
        using var admin = fixture.ClientFor(fixture.TokenForC);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/approvals/income/{item}/approve", new { })).StatusCode);
    }

    [Fact]
    public async Task Another_committees_finance_cannot_see_or_decide_an_item()
    {
        var (_, executive) = await RoleClientAsync("executive");
        using var _1 = executive;
        var item = await PendingInstalmentAsync(executive, await NewBillAsync(), 100);
        using var asA = fixture.ClientFor(fixture.TokenForA);

        Assert.Equal(HttpStatusCode.NotFound, (await asA.PostAsJsonAsync($"/api/approvals/income/{item}/approve", new { })).StatusCode);
        var listed = await asA.GetStringAsync("/api/approvals");
        Assert.DoesNotContain(item.ToString(), listed);
    }

    [Fact]
    public async Task The_queue_lists_open_items_and_a_member_sees_only_their_own()
    {
        var (_, mine) = await RoleClientAsync("executive", "Queue Mine");
        var (_, theirs) = await RoleClientAsync("executive", "Queue Theirs");
        using var _1 = mine; using var _2 = theirs;
        var myItem = await PendingInstalmentAsync(mine, await NewBillAsync(), 100);
        var theirItem = await PendingExpenseAsync(theirs, 20);
        using var admin = fixture.ClientFor(fixture.TokenForC);

        var all = await admin.GetStringAsync("/api/approvals");
        Assert.Contains(myItem.ToString(), all);
        Assert.Contains(theirItem.ToString(), all);

        var own = await mine.GetStringAsync("/api/my/approvals");
        Assert.Contains(myItem.ToString(), own);
        Assert.DoesNotContain(theirItem.ToString(), own);
    }

    [Fact]
    public async Task An_expense_approved_for_less_drops_the_difference_and_an_advance_bill_leaves_it_outstanding()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;

        var plain = await PendingExpenseAsync(executive, 500);
        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync($"/api/approvals/expense/{plain}/approve", new { amount = 400 })).StatusCode);
        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            var e = await db.Expenses.SingleAsync(x => x.Id == plain);
            Assert.Equal((400m, "approved"), (e.Amount, e.Status));
        }

        var issued = await finance.PostAsJsonAsync("/api/advances", new { issued_to_name = "Less holder", year_id = fixture.C.Year, amount = 600, issued_on = "2026-10-04" });
        var advance = (await JsonAsync(issued)).GetProperty("id").GetGuid();
        var settled = await executive.PostAsJsonAsync($"/api/advances/{advance}/settle", new { kind = "expense", amount = 250, category = "Decoration" });
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        await using var read = fixture.ContextFor(fixture.TenantC);
        var billId = (await read.Expenses.SingleAsync(e => e.AdvanceId == advance)).Id;

        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync($"/api/approvals/expense/{billId}/approve", new { amount = 200 })).StatusCode);

        var balances = await finance.GetFromJsonAsync<JsonElement>($"/api/advances?year_id={fixture.C.Year}");
        var row = balances.EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == advance);
        Assert.Equal(400m, row.GetProperty("outstanding").GetDecimal());
    }

    [Fact]
    public async Task Rejecting_an_advance_bill_removes_its_settlement_and_leaves_the_advance_out()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var issued = await finance.PostAsJsonAsync("/api/advances", new { issued_to_name = "Reject holder", year_id = fixture.C.Year, amount = 300, issued_on = "2026-10-04" });
        var advance = (await JsonAsync(issued)).GetProperty("id").GetGuid();
        await executive.PostAsJsonAsync($"/api/advances/{advance}/settle", new { kind = "expense", amount = 300, category = "Decoration" });
        await using var read = fixture.ContextFor(fixture.TenantC);
        var billId = (await read.Expenses.SingleAsync(e => e.AdvanceId == advance)).Id;

        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync($"/api/approvals/expense/{billId}/reject", new { reason = "Not a committee expense" })).StatusCode);

        await using var after = fixture.ContextFor(fixture.TenantC);
        Assert.Empty(await after.AdvanceSettlements.Where(s => s.AdvanceId == advance).ToListAsync());
        Assert.Equal("rejected", (await after.Expenses.SingleAsync(e => e.Id == billId)).Status);
        Assert.Equal("open", (await after.Advances.SingleAsync(a => a.Id == advance)).Status);
    }

    [Fact]
    public async Task An_own_pocket_claim_can_be_refunded_once_and_only_after_approval()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var claim = await PendingExpenseAsync(executive, 180, paidFrom: "own_pocket");
        var refund = new { refunded_on = "2026-10-25", mode = "Cash" };

        Assert.Equal(HttpStatusCode.Conflict, (await finance.PostAsJsonAsync($"/api/expenses/{claim}/refund", refund)).StatusCode);
        await finance.PostAsJsonAsync($"/api/approvals/expense/{claim}/approve", new { });
        Assert.Equal(HttpStatusCode.OK, (await finance.PostAsJsonAsync($"/api/expenses/{claim}/refund", refund)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await finance.PostAsJsonAsync($"/api/expenses/{claim}/refund", refund)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await finance.PostAsJsonAsync($"/api/expenses/{claim}/refund", new { })).StatusCode);
    }

    [Fact]
    public async Task A_sent_back_expense_goes_back_to_pending_when_its_owner_fixes_it()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;
        var claim = await PendingExpenseAsync(executive, 90);
        await finance.PostAsJsonAsync($"/api/approvals/expense/{claim}/send_back", new { note = "Attach the bill number" });

        var fixedUp = await executive.PatchAsJsonAsync($"/api/expenses/{claim}", new { bill_no = "B-17" });

        Assert.Equal(HttpStatusCode.OK, fixedUp.StatusCode);
        await using var db = fixture.ContextFor(fixture.TenantC);
        Assert.Equal("pending", (await db.Expenses.SingleAsync(e => e.Id == claim)).Status);
    }

    private async Task<List<(string Title, string Body)>> NotificationsAboutAsync(string needle, int expected)
    {
        for (var i = 0; i < 40; i++)
        {
            await using var db = fixture.ContextFor(fixture.TenantC);
            var rows = (await db.Notifications.Where(n => n.Body != null && n.Body.Contains(needle)).ToListAsync())
                .Select(n => (n.Title, n.Body)).ToList();
            if (rows.Count >= expected) return rows;
            await Task.Delay(100);
        }

        await using var last = fixture.ContextFor(fixture.TenantC);
        return (await last.Notifications.Where(n => n.Body != null && n.Body.Contains(needle)).ToListAsync())
            .Select(n => (n.Title, n.Body)).ToList();
    }

    private static HttpClient ClientOn(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, string token)
    {
        var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"{Utsav.Api.Authentication.SessionCookie.Name}={token}");
        client.DefaultRequestHeaders.Add(Utsav.Api.Authentication.SessionCookie.CsrfHeader, Utsav.Infrastructure.Authentication.SessionTokens.CsrfFor(token));
        return client;
    }

    [Fact]
    public async Task A_submission_notifies_the_approvers_once_and_a_decision_notifies_the_member()
    {
        // The push queue is wired through a process-wide static, so this test runs its own host to be
        // sure the notifications land in the queue it is watching, whatever other hosts have started.
        await using var host = fixture.NewHost();
        var (_, execToken) = await fixture.NewMemberAsync("Notif Exec Zed", [(fixture.TenantC, "executive", "active")], fixture.TenantC);
        var (_, finToken) = await fixture.NewMemberAsync("notif-fin", [(fixture.TenantC, "finance", "active")], fixture.TenantC);
        using var executive = ClientOn(host, execToken);
        using var finance = ClientOn(host, finToken);

        var claim = await PendingExpenseAsync(executive, 7351);

        var submitted = await NotificationsAboutAsync("Notif Exec Zed", 1);
        Assert.Single(submitted);                               // ours only, not also "Expenses added"
        Assert.Equal("Waiting for your approval", submitted[0].Title);

        await finance.PostAsJsonAsync($"/api/approvals/expense/{claim}/send_back", new { note = "Missing bill" });

        var all = await NotificationsAboutAsync("Missing bill", 1);
        Assert.Contains(all, n => n.Title == "Sent back for a fix");
    }
}
