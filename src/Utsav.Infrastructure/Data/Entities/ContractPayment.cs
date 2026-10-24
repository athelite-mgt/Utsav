using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class ContractPayment
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid ContractId { get; set; }

    public string? Milestone { get; set; }

    public decimal Amount { get; set; }

    public string? DueDate { get; set; }

    public string? PaidDate { get; set; }

    public Guid? ExpenseId { get; set; }

    public string Status { get; set; } = null!;

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Contract Contract { get; set; } = null!;

    public virtual Organization? Org { get; set; }
}
