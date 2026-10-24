using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// Spending against the feast pool — catering, decoration, prasad, the
/// coupon printing itself. Its own table rather than a flag on
/// <see cref="Expense"/>: feast money (both what comes in via
/// <see cref="Feast"/> and what goes out here) is deliberately kept out of
/// the general committee fund's Budget/Expenses/Fund Position figures, the
/// same reasoning that already gave feast income its own table instead of a
/// <c>subscriptions</c> category.
/// </summary>
public partial class FeastExpense
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    public string? Category { get; set; }

    public string? Details { get; set; }

    public decimal Amount { get; set; }

    public string? SpentOn { get; set; }

    public string? Mode { get; set; }

    public string? Comment { get; set; }

    public Guid? VendorId { get; set; }

    public string? BillNo { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual Vendor? Vendor { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}
