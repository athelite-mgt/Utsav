using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Advance
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string IssuedToName { get; set; } = null!;

    public Guid? IssuedToCollectorId { get; set; }

    public decimal Amount { get; set; }

    public string? IssuedOn { get; set; }

    public string? Purpose { get; set; }

    public string? IssuedBy { get; set; }

    public string? Mode { get; set; }

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<AdvanceSettlement> AdvanceSettlements { get; set; } = new List<AdvanceSettlement>();

    public virtual Collector? IssuedToCollector { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
