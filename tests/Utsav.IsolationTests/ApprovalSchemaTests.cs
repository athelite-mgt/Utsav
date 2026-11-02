using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data.Entities;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Money only counts once it is approved. These pin the reporting views, which
/// every dashboard, budget and advance figure is derived from. Run in
/// committee C (the one tests may change).
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class ApprovalSchemaTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Existing_rows_default_to_approved_and_feast_defaults_on()
    {
        await using var db = fixture.ContextFor(fixture.TenantA);

        Assert.All(await db.Expenses.ToListAsync(), e => Assert.Equal("approved", e.Status));
        Assert.All(await db.CollectionPayments.ToListAsync(), p => Assert.Equal("approved", p.Status));
        Assert.All(await db.Expenses.ToListAsync(), e => Assert.Equal("fund", e.PaidFrom));

        await using var identity = fixture.ContextWithNoTenant();
        Assert.True((await identity.Organizations.SingleAsync(o => o.Id == fixture.TenantA)).FeastEnabled);
    }

    [Fact]
    public async Task Budget_actuals_ignore_pending_expenses()
    {
        var category = "Approvals-" + Guid.NewGuid().ToString("N")[..6];
        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            db.BudgetLines.Add(new BudgetLine { Id = Guid.NewGuid(), OrgId = fixture.TenantC, YearId = fixture.C.Year, Category = category, Forecast = 1000 });
            db.Expenses.Add(new Expense { Id = Guid.NewGuid(), OrgId = fixture.TenantC, YearId = fixture.C.Year, Category = category, Amount = 300, Status = "approved" });
            db.Expenses.Add(new Expense { Id = Guid.NewGuid(), OrgId = fixture.TenantC, YearId = fixture.C.Year, Category = category, Amount = 700, Status = "pending" });
            db.Expenses.Add(new Expense { Id = Guid.NewGuid(), OrgId = fixture.TenantC, YearId = fixture.C.Year, Category = category, Amount = 50, Status = "rejected" });
            await db.SaveChangesAsync();
        }

        await using var read = fixture.ContextFor(fixture.TenantC);
        var line = await read.VBudgetVsActuals.SingleAsync(v => v.Category == category);
        Assert.Equal(300m, line.Actual);
    }

    [Fact]
    public async Task A_pending_settlement_bill_leaves_the_advance_outstanding_until_approved()
    {
        Guid advanceId, expenseId;
        await using (var db = fixture.ContextFor(fixture.TenantC))
        {
            advanceId = Guid.NewGuid();
            expenseId = Guid.NewGuid();
            db.Advances.Add(new Advance
            {
                Id = advanceId, OrgId = fixture.TenantC, YearId = fixture.C.Year, IssuedToName = "Schema holder", Amount = 1000, Status = "open",
            });
            db.Expenses.Add(new Expense
            {
                Id = expenseId, OrgId = fixture.TenantC, YearId = fixture.C.Year, Category = "Decoration", Amount = 400,
                AdvanceId = advanceId, Status = "pending",
            });
            db.AdvanceSettlements.Add(new AdvanceSettlement
            {
                Id = Guid.NewGuid(), OrgId = fixture.TenantC, AdvanceId = advanceId, Kind = "expense", ExpenseId = expenseId, Amount = 400,
            });
            db.AdvanceSettlements.Add(new AdvanceSettlement
            {
                Id = Guid.NewGuid(), OrgId = fixture.TenantC, AdvanceId = advanceId, Kind = "return", Amount = 100,
            });
            await db.SaveChangesAsync();
        }

        await using (var read = fixture.ContextFor(fixture.TenantC))
        {
            var pending = await read.VAdvanceBalances.SingleAsync(v => v.Id == advanceId);
            // Only the cash return counts; the unapproved bill has not accounted for its ₹400.
            Assert.Equal(100m, pending.Settled);
            Assert.Equal(900m, pending.Outstanding);
        }

        await using (var write = fixture.ContextFor(fixture.TenantC))
        {
            var expense = await write.Expenses.SingleAsync(e => e.Id == expenseId);
            expense.Status = "approved";
            await write.SaveChangesAsync();
        }

        await using var after = fixture.ContextFor(fixture.TenantC);
        var approved = await after.VAdvanceBalances.SingleAsync(v => v.Id == advanceId);
        Assert.Equal(500m, approved.Settled);
        Assert.Equal(500m, approved.Outstanding);
    }
}
