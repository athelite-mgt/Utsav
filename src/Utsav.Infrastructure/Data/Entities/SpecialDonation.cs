using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class SpecialDonation
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string Name { get; set; } = null!;

    public string? Contact { get; set; }

    public string? BillNo { get; set; }

    public decimal? Projected { get; set; }

    public decimal? Billed { get; set; }

    public decimal? Collected { get; set; }

    public decimal? Due { get; set; }

    public string? Mode { get; set; }

    public string? CollectedOn { get; set; }

    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
