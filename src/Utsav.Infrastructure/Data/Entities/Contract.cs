using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Contract
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public Guid VendorId { get; set; }

    public string Title { get; set; } = null!;

    public string? Category { get; set; }

    public decimal ContractAmount { get; set; }

    public decimal AdvancePaid { get; set; }

    public string? StartDate { get; set; }

    public string? EndDate { get; set; }

    public string Status { get; set; } = null!;

    public string? Terms { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<ContractPayment> ContractPayments { get; set; } = new List<ContractPayment>();

    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    public virtual ICollection<VendorIssue> VendorIssues { get; set; } = new List<VendorIssue>();

    public virtual Organization? Org { get; set; }

    public virtual Vendor Vendor { get; set; } = null!;

    public virtual PujaYear Year { get; set; } = null!;
}
