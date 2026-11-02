using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// An expense recorded by anyone but Finance/Admin is a claim until approved,
/// and advances stay in the cashier's hands. Run in committee C.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class ExpenseApprovalTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(Guid UserId, HttpClient Client)> RoleClientAsync(string role)
    {
        var (userId, token) = await fixture.NewMemberAsync($"expense-{role}", [(fixture.TenantC, role, "active")], fixture.TenantC);
        return (userId, fixture.ClientFor(token));
    }

    private async Task<Guid> ExpenseAsync(HttpClient client, decimal amount, object? extra = null)
    {
        var response = await client.PostAsJsonAsync("/api/expenses", new
        {
            year_id = fixture.C.Year, category = "Decoration", details = "Approval test", amount, spent_on = "2026-10-20", mode = "Cash",
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Infrastructure_Expense> ReadAsync(Guid id)
    {
        await using var db = fixture.ContextFor(fixture.TenantC);
        var e = await db.Expenses.SingleAsync(x => x.Id == id);
        return new Infrastructure_Expense(e.Status, e.PaidFrom, e.RecordedByUserId, e.Amount);
    }

    private sealed record Infrastructure_Expense(string Status, string PaidFrom, Guid? RecordedBy, decimal Amount);

    [Fact]
    public async Task An_executives_expense_is_pending_and_stamped_with_who_recorded_it()
    {
        var (userId, executive) = await RoleClientAsync("executive");
        using var _ = executive;

        var id = await ExpenseAsync(executive, 250);

        var row = await ReadAsync(id);
        Assert.Equal("pending", row.Status);
        Assert.Equal(userId, row.RecordedBy);
        Assert.Equal("fund", row.PaidFrom);
    }

    [Fact]
    public async Task A_finance_expense_is_approved_on_save()
    {
        var (_, finance) = await RoleClientAsync("finance");
        using var _ = finance;

        Assert.Equal("approved", (await ReadAsync(await ExpenseAsync(finance, 90))).Status);
    }

    [Fact]
    public async Task The_status_cannot_be_chosen_by_the_caller_and_paid_from_is_validated()
    {
        var (_, executive) = await RoleClientAsync("executive");
        using var _ = executive;

        var response = await executive.PostAsJsonAsync("/api/expenses", new
        {
            year_id = fixture.C.Year, category = "Decoration", amount = 10, status = "approved", recorded_by_name = "Someone Else",
            paid_from = "own_pocket",
        });
        var id = (await JsonAsync(response)).GetProperty("id").GetGuid();

        var row = await ReadAsync(id);
        Assert.Equal("pending", row.Status);
        Assert.Equal("own_pocket", row.PaidFrom);

        var bad = await executive.PostAsJsonAsync("/api/expenses", new { year_id = fixture.C.Year, amount = 1, paid_from = "elsewhere" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task A_member_can_fix_or_withdraw_their_own_open_claim_but_not_approved_or_others()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, other) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = other; using var _3 = finance;

        var mine = await ExpenseAsync(executive, 100);
        var approved = await ExpenseAsync(finance, 100);

        Assert.Equal(HttpStatusCode.OK, (await executive.PatchAsJsonAsync($"/api/expenses/{mine}", new { amount = 120 })).StatusCode);
        Assert.Equal(120m, (await ReadAsync(mine)).Amount);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.PatchAsJsonAsync($"/api/expenses/{mine}", new { amount = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await executive.PatchAsJsonAsync($"/api/expenses/{approved}", new { amount = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await executive.DeleteAsync($"/api/expenses/{approved}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await executive.DeleteAsync($"/api/expenses/{mine}")).StatusCode);
    }

    [Fact]
    public async Task Secretariat_entries_wait_and_approved_entries_are_locked_to_them_too()
    {
        var (_, secretariat) = await RoleClientAsync("secretariat");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = secretariat; using var _2 = finance;

        Assert.Equal("pending", (await ReadAsync(await ExpenseAsync(secretariat, 15))).Status);

        var approved = await ExpenseAsync(finance, 40);
        Assert.Equal(HttpStatusCode.Forbidden, (await secretariat.PatchAsJsonAsync($"/api/expenses/{approved}", new { amount = 1 })).StatusCode);
    }

    [Fact]
    public async Task Only_finance_can_issue_an_advance_or_take_cash_back()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, admin) = await RoleClientAsync("admin");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = admin; using var _3 = finance;

        var body = new { issued_to_name = "Approval holder", year_id = fixture.C.Year, amount = 800, issued_on = "2026-10-04" };
        Assert.Equal(HttpStatusCode.Forbidden, (await executive.PostAsJsonAsync("/api/advances", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/advances", body)).StatusCode);

        var issued = await finance.PostAsJsonAsync("/api/advances", body);
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var advance = (await JsonAsync(issued)).GetProperty("id").GetGuid();

        var cashBack = await executive.PostAsJsonAsync($"/api/advances/{advance}/settle", new { kind = "return", amount = 50 });
        Assert.Equal(HttpStatusCode.Forbidden, cashBack.StatusCode);
    }

    [Fact]
    public async Task A_members_bill_against_an_advance_is_pending_and_leaves_the_advance_outstanding()
    {
        var (_, executive) = await RoleClientAsync("executive");
        var (_, finance) = await RoleClientAsync("finance");
        using var _1 = executive; using var _2 = finance;

        var issued = await finance.PostAsJsonAsync("/api/advances", new
        {
            issued_to_name = "Settle holder", year_id = fixture.C.Year, amount = 600, issued_on = "2026-10-04",
        });
        var advance = (await JsonAsync(issued)).GetProperty("id").GetGuid();

        var settled = await executive.PostAsJsonAsync($"/api/advances/{advance}/settle", new
        {
            kind = "expense", amount = 250, category = "Decoration", details = "Flowers", spent_on = "2026-10-20",
        });
        Assert.True(settled.StatusCode == HttpStatusCode.OK, await settled.Content.ReadAsStringAsync());
        Assert.Equal(600m, (await JsonAsync(settled)).GetProperty("outstanding").GetDecimal());

        await using var db = fixture.ContextFor(fixture.TenantC);
        var expense = await db.Expenses.SingleAsync(e => e.AdvanceId == advance);
        Assert.Equal("pending", expense.Status);

        var byFinance = await finance.PostAsJsonAsync($"/api/advances/{advance}/settle", new
        {
            kind = "expense", amount = 100, category = "Decoration", details = "Cloth", spent_on = "2026-10-20",
        });
        Assert.Equal(500m, (await JsonAsync(byFinance)).GetProperty("outstanding").GetDecimal());
    }
}
