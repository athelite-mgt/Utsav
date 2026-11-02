using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data.Entities;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// The fund holds only approved money, and every rupee is counted exactly once
/// as it moves pending → approved → refunded, or advance → bill → approved.
/// Each test builds its own clean year in committee C.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class FundPositionApprovalTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly List<Guid> _years = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Removes the years this class made, so committee C's other tests see only their own.</summary>
    public async Task DisposeAsync()
    {
        await using var db = fixture.ContextFor(fixture.TenantC);
        foreach (var year in _years)
        {
            var advances = await db.Advances.Where(a => a.YearId == year).Select(a => a.Id).ToListAsync();
            db.AdvanceSettlements.RemoveRange(db.AdvanceSettlements.Where(x => advances.Contains(x.AdvanceId)));
            db.Expenses.RemoveRange(db.Expenses.Where(x => x.YearId == year));
            db.Advances.RemoveRange(db.Advances.Where(x => x.YearId == year));
            db.CollectionPayments.RemoveRange(db.CollectionPayments.Where(x => x.YearId == year));
            await db.SaveChangesAsync();
            db.PujaYears.RemoveRange(db.PujaYears.Where(x => x.Id == year));
            await db.SaveChangesAsync();
        }
    }

    private static int _nextYear = 2200;

    private async Task<Guid> NewYearAsync()
    {
        var id = Guid.NewGuid();
        await using var db = fixture.ContextFor(fixture.TenantC);
        db.PujaYears.Add(new PujaYear { Id = id, OrgId = fixture.TenantC, Year = Interlocked.Increment(ref _nextYear), Name = "Fund test", IsActive = false });
        await db.SaveChangesAsync();
        _years.Add(id);
        return id;
    }

    private async Task AddInstalmentAsync(Guid year, decimal amount, string mode, string status, string by = "Member One")
    {
        await using var db = fixture.ContextFor(fixture.TenantC);
        db.CollectionPayments.Add(new CollectionPayment
        {
            Id = Guid.NewGuid(), OrgId = fixture.TenantC, YearId = year, ParentType = "special_donation", ParentId = Guid.NewGuid(),
            Amount = amount, Mode = mode, Status = status, RecordedByName = by,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AddExpenseAsync(Guid year, decimal amount, string status, string paidFrom = "fund", string by = "Member One")
    {
        var id = Guid.NewGuid();
        await using var db = fixture.ContextFor(fixture.TenantC);
        db.Expenses.Add(new Expense
        {
            Id = id, OrgId = fixture.TenantC, YearId = year, Category = "Decoration", Amount = amount, Mode = "Cash",
            Status = status, PaidFrom = paidFrom, RecordedByName = by,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<JsonElement> PositionAsync(Guid year)
    {
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var response = await admin.GetAsync($"/api/fund_position?year_id={year}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static decimal Cash(JsonElement p) => p.GetProperty("cash").GetProperty("balance").GetDecimal();
    private static decimal Bank(JsonElement p) => p.GetProperty("bank").GetProperty("balance").GetDecimal();

    [Fact]
    public async Task Pending_money_is_held_by_the_member_and_not_in_the_fund()
    {
        var year = await NewYearAsync();
        await AddInstalmentAsync(year, 1000, "Cash", "approved");
        await AddInstalmentAsync(year, 400, "Cash", "pending", by: "Rakesh Test");
        await AddInstalmentAsync(year, 250, "GPay", "pending", by: "Rakesh Test");
        await AddInstalmentAsync(year, 60, "Cash", "rejected", by: "Rakesh Test");
        await AddExpenseAsync(year, 300, "approved");
        await AddExpenseAsync(year, 999, "pending");

        var p = await PositionAsync(year);

        Assert.Equal(700m, Cash(p));
        Assert.Equal(0m, Bank(p));
        var pending = p.GetProperty("pending");
        Assert.Equal(400m, pending.GetProperty("heldTotal").GetDecimal());
        Assert.Equal("Rakesh Test", pending.GetProperty("heldByMembers")[0].GetProperty("name").GetString());
        Assert.Equal(250m, pending.GetProperty("onlinePending").GetDecimal());
        Assert.Equal(1, pending.GetProperty("pendingExpenses").GetInt32());
        Assert.Equal(999m, pending.GetProperty("pendingExpensesTotal").GetDecimal());
    }

    [Fact]
    public async Task Approving_a_pending_instalment_moves_it_into_the_fund_exactly_once()
    {
        var year = await NewYearAsync();
        await AddInstalmentAsync(year, 500, "Cash", "pending");
        Assert.Equal(0m, Cash(await PositionAsync(year)));

        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            var row = await db.CollectionPayments.SingleAsync(p => p.YearId == year);
            row.Status = "approved";
            await db.SaveChangesAsync();
        }

        var after = await PositionAsync(year);
        Assert.Equal(500m, Cash(after));
        Assert.Equal(0m, after.GetProperty("pending").GetProperty("heldTotal").GetDecimal());
    }

    [Fact]
    public async Task An_own_pocket_claim_is_owed_until_refunded_then_leaves_the_fund_once()
    {
        var year = await NewYearAsync();
        await AddInstalmentAsync(year, 1000, "Cash", "approved");
        var claim = await AddExpenseAsync(year, 200, "approved", paidFrom: "own_pocket", by: "Meera Test");

        var owed = await PositionAsync(year);
        Assert.Equal(1000m, Cash(owed));                       // nothing has left the box
        Assert.Equal(200m, owed.GetProperty("pending").GetProperty("owedTotal").GetDecimal());
        Assert.Equal("Meera Test", owed.GetProperty("pending").GetProperty("owedToMembers")[0].GetProperty("name").GetString());

        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            var e = await db.Expenses.SingleAsync(x => x.Id == claim);
            e.RefundedOn = "2026-10-25";
            e.RefundMode = "Cash";
            await db.SaveChangesAsync();
        }

        var refunded = await PositionAsync(year);
        Assert.Equal(800m, Cash(refunded));
        Assert.Equal(0m, refunded.GetProperty("pending").GetProperty("owedTotal").GetDecimal());
    }

    [Fact]
    public async Task A_refund_made_from_the_bank_leaves_the_bank_side()
    {
        var year = await NewYearAsync();
        await AddInstalmentAsync(year, 1000, "GPay", "approved");
        var claim = await AddExpenseAsync(year, 300, "approved", paidFrom: "own_pocket");
        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            var e = await db.Expenses.SingleAsync(x => x.Id == claim);
            e.RefundedOn = "2026-10-25";
            e.RefundMode = "GPay";
            await db.SaveChangesAsync();
        }

        var p = await PositionAsync(year);
        Assert.Equal(700m, Bank(p));
        Assert.Equal(0m, Cash(p));
    }

    [Fact]
    public async Task An_advance_bill_moves_money_from_out_with_the_member_to_spent_without_double_counting()
    {
        var year = await NewYearAsync();
        await AddInstalmentAsync(year, 1000, "Cash", "approved");
        var advance = Guid.NewGuid();
        var billId = Guid.NewGuid();
        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            db.Advances.Add(new Advance { Id = advance, OrgId = fixture.TenantC, YearId = year, IssuedToName = "Float holder", Amount = 500, Mode = "Cash", Status = "open" });
            db.Expenses.Add(new Expense
            {
                Id = billId, OrgId = fixture.TenantC, YearId = year, Category = "Decoration", Amount = 200, Mode = "Cash",
                AdvanceId = advance, Status = "pending",
            });
            db.AdvanceSettlements.Add(new AdvanceSettlement { Id = Guid.NewGuid(), OrgId = fixture.TenantC, AdvanceId = advance, Kind = "expense", ExpenseId = billId, Amount = 200 });
            await db.SaveChangesAsync();
        }

        // Pending bill: the whole ₹500 is still out with the member.
        Assert.Equal(500m, Cash(await PositionAsync(year)));

        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            (await db.Expenses.SingleAsync(e => e.Id == billId)).Status = "approved";
            await db.SaveChangesAsync();
        }

        // Approved: ₹200 spent, ₹300 still out — the same ₹500 gone from the box, counted once.
        var p = await PositionAsync(year);
        Assert.Equal(500m, Cash(p));
        Assert.Equal(200m, p.GetProperty("cash").GetProperty("spent").GetDecimal());
        Assert.Equal(300m, p.GetProperty("cash").GetProperty("advancesOut").GetDecimal());
    }
}
