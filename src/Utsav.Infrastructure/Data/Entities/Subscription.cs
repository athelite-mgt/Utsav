using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

public partial class Subscription
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid DonorId { get; set; }

    public Guid YearId { get; set; }

    /// <summary>
    /// Splits the one table into the two sections the committee sees:
    /// "membership" (the household subscription round) and "special" (major
    /// one-off gifts). Kept as a column rather than a second table so moving a
    /// row between sections is a single UPDATE that preserves the donor link,
    /// bill number and prior-year history. Only the projection rollover treats
    /// them differently — see AdminEndpoints.RolloverAsync.
    /// </summary>
    public string Category { get; set; } = SubscriptionCategory.Membership;

    public decimal? PriorBilled { get; set; }

    public decimal? Projected { get; set; }

    public string? BillNo { get; set; }

    /// <summary>
    /// Donor is sitting out this puja year — e.g. moved away, deceased, or a
    /// household the committee has agreed not to bill. Kept distinct from
    /// simply billing zero so these show up as a deliberate exception rather
    /// than an accidentally-missed donor in the Not Billed worklist.
    /// </summary>
    public bool NotEligible { get; set; }

    public decimal? Billed { get; set; }

    public string? BilledOn { get; set; }

    public decimal? Collected { get; set; }

    public decimal? Due { get; set; }

    public string? Mode { get; set; }

    public string? CollectedOn { get; set; }

    public Guid? CollectorId { get; set; }

    public string? Comments { get; set; }

    public string? History { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Collector? Collector { get; set; }

    public virtual Donor Donor { get; set; } = null!;

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
