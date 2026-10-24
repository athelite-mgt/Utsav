using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Offering
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string Name { get; set; } = null!;

    public string? Contact { get; set; }

    public string? OfferingType { get; set; }

    public string? PujaDay { get; set; }

    /// <summary>
    /// Estimated/market value of the in-kind gift — not money collected, just
    /// a figure to plan next year's budget from (e.g. rollover projections).
    /// </summary>
    public decimal? Value { get; set; }

    public string? Details { get; set; }

    public string? ExpectedDate { get; set; }

    public string? ReceivedDate { get; set; }

    public string? ReceivedBy { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
