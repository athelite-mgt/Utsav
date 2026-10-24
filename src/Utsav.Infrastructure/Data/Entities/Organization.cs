using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Organization
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Slug { get; set; } = null!;

    public string? City { get; set; }

    public string? EntraTenantId { get; set; }

    public bool IsActive { get; set; }

    /// <summary>Committee logo, a tenant-prefixed blob URL. Null falls back to an initial.</summary>
    public string? LogoUrl { get; set; }

    /// <summary>Brand colour as a #rgb or #rrggbb hex literal, validated on write.</summary>
    public string? AccentColour { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AdBooking> AdBookings { get; set; } = new List<AdBooking>();

    public virtual ICollection<AdvanceSettlement> AdvanceSettlements { get; set; } = new List<AdvanceSettlement>();

    public virtual ICollection<Advance> Advances { get; set; } = new List<Advance>();

    public virtual ICollection<Advertiser> Advertisers { get; set; } = new List<Advertiser>();

    public virtual ICollection<BudgetLine> BudgetLines { get; set; } = new List<BudgetLine>();

    public virtual ICollection<CashLedger> CashLedgers { get; set; } = new List<CashLedger>();

    public virtual ICollection<Collector> Collectors { get; set; } = new List<Collector>();

    public virtual ICollection<ContractPayment> ContractPayments { get; set; } = new List<ContractPayment>();

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual ICollection<Donor> Donors { get; set; } = new List<Donor>();

    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    public virtual ICollection<FeastExpense> FeastExpenses { get; set; } = new List<FeastExpense>();

    public virtual ICollection<Feast> Feasts { get; set; } = new List<Feast>();

    public virtual ICollection<FeastToken> FeastTokens { get; set; } = new List<FeastToken>();

    public virtual ICollection<FundTransfer> FundTransfers { get; set; } = new List<FundTransfer>();

    public virtual ICollection<Lookup> Lookups { get; set; } = new List<Lookup>();

    public virtual ICollection<Offering> Offerings { get; set; } = new List<Offering>();

    public virtual ICollection<OrgMember> OrgMembers { get; set; } = new List<OrgMember>();

    public virtual ICollection<OrgRoleMapping> OrgRoleMappings { get; set; } = new List<OrgRoleMapping>();

    public virtual ICollection<PujaSchedule> PujaSchedules { get; set; } = new List<PujaSchedule>();

    public virtual ICollection<PujaYear> PujaYears { get; set; } = new List<PujaYear>();

    public virtual ICollection<PushSubscriptionRecord> PushSubscriptionRecords { get; set; } = new List<PushSubscriptionRecord>();

    public virtual ICollection<ApiKey> ApiKeys { get; set; } = new List<ApiKey>();

    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();

    public virtual ICollection<SpecialDonation> SpecialDonations { get; set; } = new List<SpecialDonation>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual ICollection<Vendor> Vendors { get; set; } = new List<Vendor>();

    public virtual ICollection<WorkflowItem> WorkflowItems { get; set; } = new List<WorkflowItem>();

    public virtual ICollection<WorkflowNote> WorkflowNotes { get; set; } = new List<WorkflowNote>();

    public virtual ICollection<Zone> Zones { get; set; } = new List<Zone>();
}
