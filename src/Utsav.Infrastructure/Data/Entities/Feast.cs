using System;
using System.Collections.Generic;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// Feast collection — one row per donor per puja year, billed and collected
/// the same way as Membership/Special Donation (see <see cref="Subscription"/>),
/// but its own table rather than a third <c>subscriptions</c> category: that
/// table's (donor_id, year_id) unique index would otherwise block a donor from
/// being billed for both membership and feast in the same year. No prior-year
/// carry-over fields — feast collection starts fresh from the year it's
/// introduced, with no rollover/raise concept to track.
/// </summary>
public partial class Feast
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid DonorId { get; set; }

    public Guid YearId { get; set; }

    public string? BillNo { get; set; }

    /// <summary>Same meaning as <see cref="Subscription.NotEligible"/> — a deliberate "sitting this one out", not a missed bill.</summary>
    public bool NotEligible { get; set; }

    public decimal? Billed { get; set; }

    public string? BilledOn { get; set; }

    public decimal? Collected { get; set; }

    public decimal? Due { get; set; }

    public string? Mode { get; set; }

    public string? CollectedOn { get; set; }

    public Guid? CollectorId { get; set; }

    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Collector? Collector { get; set; }

    public virtual Donor Donor { get; set; } = null!;

    public virtual Organization? Org { get; set; }

    public virtual ICollection<FeastToken> Tokens { get; set; } = new List<FeastToken>();

    public virtual PujaYear Year { get; set; } = null!;
}
