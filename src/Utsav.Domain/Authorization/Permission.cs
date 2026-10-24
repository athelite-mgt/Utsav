namespace Utsav.Domain.Authorization;

/// <summary>
/// A capability an endpoint requires. Endpoints are gated on these, never on
/// role names directly — so changing who can do what is a change to
/// <see cref="PermissionMatrix"/> alone, and can be exhaustively tested.
/// </summary>
public enum Permission
{
    /// <summary>
    /// See committee data: dashboard, fund position, and every module's list
    /// view. Every active member has this; access is gated by membership
    /// status, not by role.
    /// </summary>
    Read,

    /// <summary>
    /// Committee configuration: lookups, zones, collectors, puja years,
    /// committee name, and year rollover. Admin-exclusive — this is the
    /// "full customization control" from the brief.
    /// </summary>
    ManageSettings,

    /// <summary>Approve/deny access requests and assign roles.</summary>
    ManageUsers,

    /// <summary>Chanda subscriptions, ad bookings, special donations, offerings.</summary>
    WriteIncome,

    /// <summary>
    /// Expenses, budget lines, vendors, contracts, contract payments,
    /// cash ledger, fund transfers.
    /// </summary>
    WriteFinance,

    /// <summary>Workflow items, workflow notes, and the puja-day duty roster.</summary>
    WriteWorkflow,

    /// <summary>Cash advances and their settlements.</summary>
    WriteAdvances,

    /// <summary>Read the audit trail.</summary>
    ReadAudit,

    /// <summary>
    /// Ask Asura, the AI reporting assistant, questions about the committee's
    /// books. Read-only by construction — Asura cannot write anything — but
    /// gated separately from <see cref="Read"/> because it reads <i>broadly</i>:
    /// one question can span income, spending, vendors and advances at once.
    /// Treasurer-level and above only, which is why Executive and Viewer are
    /// excluded despite both being able to read every individual page.
    /// </summary>
    UseAsura,
}
