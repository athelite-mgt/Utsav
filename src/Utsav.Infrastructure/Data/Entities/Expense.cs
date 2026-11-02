using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Expense
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string? Category { get; set; }

    public string? Details { get; set; }

    public decimal Amount { get; set; }

    public string? SpentOn { get; set; }

    public string? Mode { get; set; }

    public string? Comment { get; set; }

    public Guid? VendorId { get; set; }

    public Guid? ContractId { get; set; }

    public Guid? AdvanceId { get; set; }

    public string? BillNo { get; set; }

    /// <summary>approved | pending | sent_back | rejected — only approved money reaches the fund.</summary>
    public string Status { get; set; } = "approved";

    public Guid? DecidedByUserId { get; set; }

    public string? DecidedByName { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? DecisionNote { get; set; }

    /// <summary>fund | own_pocket — an own-pocket claim is owed back to the member once approved.</summary>
    public string PaidFrom { get; set; } = "fund";

    public Guid? RecordedByUserId { get; set; }

    public string? RecordedByName { get; set; }

    public string? RefundedOn { get; set; }

    public string? RefundMode { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<AdvanceSettlement> AdvanceSettlements { get; set; } = new List<AdvanceSettlement>();

    public virtual Contract? Contract { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual Vendor? Vendor { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
