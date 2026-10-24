using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// One printed, physical feast entry token — the committee prints a batch of
/// these before knowing who they'll go to, hands them out door to door, then
/// assigns each one to a donor's <see cref="Feast"/> record afterward (on the
/// spot or in a batch later). At the fooding-area entrance, scanning or
/// typing the code redeems it once, attributing the entry to that donor's
/// house and making a repeat scan of the same token a no-op.
/// </summary>
public partial class FeastToken
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid YearId { get; set; }

    /// <summary>Position within this year's print run — what "token 3 of 50" means.</summary>
    public int SeqNo { get; set; }

    /// <summary>Human-readable + QR-encoded identifier, e.g. "F2026-0001". Unique across the whole table.</summary>
    public string Code { get; set; } = null!;

    /// <summary>Which donor's feast record this token is attributed to — null until assigned.</summary>
    public Guid? FeastId { get; set; }

    /// <summary>"unassigned" | "assigned" | "redeemed" | "void" — see <see cref="FeastTokenStatus"/>.</summary>
    public string Status { get; set; } = FeastTokenStatus.Unassigned;

    public DateTime? AssignedAt { get; set; }

    public DateTime? RedeemedAt { get; set; }

    /// <summary>Who scanned this token in at the gate — the app's own "collected by" pattern, never free-typed.</summary>
    public Guid? RedeemedByUserId { get; set; }

    public string? RedeemedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Feast? FeastRecord { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual PujaYear Year { get; set; } = null!;
}

/// <summary>The four states a token moves through — print → assign → redeem, with void as the only side exit.</summary>
public static class FeastTokenStatus
{
    public const string Unassigned = "unassigned";
    public const string Assigned = "assigned";
    public const string Redeemed = "redeemed";
    public const string Void = "void";
}
