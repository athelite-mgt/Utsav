using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// One dated instalment of a collection — the "what came in, on what date, how"
/// detail the committee needs. A membership subscription, ad booking or other-
/// income record can have several: ₹500 cash on Sasthi, ₹500 GPay on Nabami.
/// The parent's <c>collected</c> column is kept as the sum of these, so every
/// existing reader (dashboard, reports, due calc) is untouched; the fund
/// position reads these rows directly so each instalment lands in the right
/// cash/bank pocket by its own mode.
///
/// Polymorphic by (<see cref="ParentType"/>, <see cref="ParentId"/>) rather
/// than three separate tables, since the shape is identical across all three
/// income streams. <see cref="YearId"/> is denormalised on so the fund
/// position and reports can filter by year without joining back to the parent.
/// </summary>
public partial class CollectionPayment
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    /// <summary>"subscription", "ad_booking" or "special_donation".</summary>
    public string ParentType { get; set; } = null!;

    public Guid ParentId { get; set; }

    public decimal Amount { get; set; }

    public string? PaidOn { get; set; }

    public string? Mode { get; set; }

    /// <summary>The signed-in user who recorded this instalment — the app's "collected by".</summary>
    public Guid? RecordedByUserId { get; set; }

    public string? RecordedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
