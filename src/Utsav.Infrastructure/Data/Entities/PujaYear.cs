using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class PujaYear
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public int Year { get; set; }

    public string? Name { get; set; }

    public decimal? Target { get; set; }

    public decimal ProjectionPct { get; set; }

    /// <summary>
    /// Cash physically in hand at the moment this puja year began — carried
    /// forward from the previous year's close. Not this year's income; folded
    /// into the fund position so "balance in hand" reflects reality.
    /// </summary>
    public decimal OpeningCashBalance { get; set; }

    /// <summary>Bank/online balance carried forward into this puja year. See <see cref="OpeningCashBalance"/>.</summary>
    public decimal OpeningBankBalance { get; set; }

    public bool IsActive { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AdBooking> AdBookings { get; set; } = new List<AdBooking>();

    public virtual ICollection<Advance> Advances { get; set; } = new List<Advance>();

    public virtual ICollection<BudgetLine> BudgetLines { get; set; } = new List<BudgetLine>();

    public virtual ICollection<CashLedger> CashLedgers { get; set; } = new List<CashLedger>();

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    public virtual ICollection<Feast> Feasts { get; set; } = new List<Feast>();

    public virtual ICollection<FeastExpense> FeastExpenses { get; set; } = new List<FeastExpense>();

    public virtual ICollection<FeastToken> FeastTokens { get; set; } = new List<FeastToken>();

    public virtual ICollection<FundTransfer> FundTransfers { get; set; } = new List<FundTransfer>();

    public virtual ICollection<Offering> Offerings { get; set; } = new List<Offering>();

    public virtual Organization? Org { get; set; }

    public virtual ICollection<PujaSchedule> PujaSchedules { get; set; } = new List<PujaSchedule>();

    public virtual ICollection<SpecialDonation> SpecialDonations { get; set; } = new List<SpecialDonation>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual ICollection<WorkflowItem> WorkflowItems { get; set; } = new List<WorkflowItem>();
}
