namespace Utsav.Domain.Authorization;

/// <summary>
/// Who may confirm money, and what status a new entry starts in. The cashier
/// (Finance) and Admin approve; everything anyone else records is pending until
/// one of them acts, so the dashboard never counts money no approver has seen.
/// </summary>
public static class ApprovalPolicy
{
    public const string Approved = "approved";
    public const string Pending = "pending";
    public const string SentBack = "sent_back";
    public const string Rejected = "rejected";

    public static bool CanApprove(AppRole role) => PermissionMatrix.Allows(role, Permission.ApproveMoney);

    /// <summary>The status an entry recorded by this role is saved with.</summary>
    public static string StatusFor(AppRole role) => CanApprove(role) ? Approved : Pending;

    /// <summary>Statuses that still await a decision (or the member's fix).</summary>
    public static bool IsOpen(string status) => status is Pending or SentBack;
}
