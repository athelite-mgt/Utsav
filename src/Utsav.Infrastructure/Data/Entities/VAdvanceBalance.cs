using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class VAdvanceBalance
{
    public Guid? Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid? YearId { get; set; }

    public string? IssuedToName { get; set; }

    public Guid? IssuedToCollectorId { get; set; }

    public decimal? Amount { get; set; }

    public string? IssuedOn { get; set; }

    public string? Purpose { get; set; }

    public string? IssuedBy { get; set; }

    public string? Mode { get; set; }

    public string? Status { get; set; }

    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public decimal? Settled { get; set; }

    public decimal? Spent { get; set; }

    public decimal? Returned { get; set; }

    public decimal? Outstanding { get; set; }
}
