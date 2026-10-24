using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class AdvanceSettlement
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid AdvanceId { get; set; }

    public string Kind { get; set; } = null!;

    public Guid? ExpenseId { get; set; }

    public decimal Amount { get; set; }

    public string? SettledOn { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Advance Advance { get; set; } = null!;

    public virtual Expense? Expense { get; set; }

    public virtual Organization? Org { get; set; }
}
