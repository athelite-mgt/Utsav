using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class BudgetLine
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string Category { get; set; } = null!;

    public string? Notes { get; set; }

    public decimal? Forecast { get; set; }

    public decimal? ContractAmount { get; set; }

    public int? SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
