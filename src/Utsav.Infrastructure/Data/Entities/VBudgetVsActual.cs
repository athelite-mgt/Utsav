using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class VBudgetVsActual
{
    public Guid? Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid? YearId { get; set; }

    public string? Category { get; set; }

    public string? Notes { get; set; }

    public decimal? Forecast { get; set; }

    public decimal? ContractAmount { get; set; }

    public int? SortOrder { get; set; }

    public decimal? Actual { get; set; }

    public decimal? Remaining { get; set; }

    public decimal? PriorActual { get; set; }

    /// <summary>Live total of every non-cancelled vendor contract in this category — see DatabaseViews.CreateBudgetVsActual.</summary>
    public decimal? Contracted { get; set; }
}
