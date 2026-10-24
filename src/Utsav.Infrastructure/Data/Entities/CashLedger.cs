using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class CashLedger
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string Party { get; set; } = null!;

    public decimal Amount { get; set; }

    public string? EntryDate { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
