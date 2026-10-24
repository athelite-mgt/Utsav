using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Vendor
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public string Name { get; set; } = null!;

    public string? Contact { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? GstNo { get; set; }

    public string? Category { get; set; }

    public bool Active { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Contract> Contracts { get; set; } = new List<Contract>();

    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();

    public virtual ICollection<FeastExpense> FeastExpenses { get; set; } = new List<FeastExpense>();

    public virtual ICollection<VendorIssue> VendorIssues { get; set; } = new List<VendorIssue>();

    public virtual Organization? Org { get; set; }
}
