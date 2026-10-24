namespace Utsav.Domain.Authorization;

/// <summary>
/// A member's role within the committee. Stored on the membership
/// (org_members.role), not on the user, so one person can hold a different
/// role in a different committee.
///
/// The wire/database values are the lowercase names below. The two the
/// production database already contains — "admin" and "viewer" — keep their
/// exact meaning, so no existing row needs rewriting when the CHECK
/// constraint widens to accept the other three.
/// </summary>
public enum AppRole
{
    /// <summary>Full control, including committee configuration.</summary>
    Admin,

    /// <summary>Everything Admin can do except configuration; can manage users.</summary>
    Secretariat,

    /// <summary>Owns the money: income, expenses, budget, vendors, contracts.</summary>
    Finance,

    /// <summary>Reads everything; writes workflow, schedule and advances.</summary>
    Executive,

    /// <summary>Read-only, everywhere.</summary>
    Viewer,
}

public static class AppRoleExtensions
{
    /// <summary>The lowercase token stored in org_members.role and sent to the frontend.</summary>
    public static string ToWire(this AppRole role) => role switch
    {
        AppRole.Admin => "admin",
        AppRole.Secretariat => "secretariat",
        AppRole.Finance => "finance",
        AppRole.Executive => "executive",
        AppRole.Viewer => "viewer",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unmapped role"),
    };

    /// <summary>
    /// Parses a role token from the database or a request body. Unknown values
    /// fail rather than defaulting: silently treating an unrecognised role as
    /// Viewer would hide a bad migration, and treating it as Admin would be a
    /// privilege-escalation bug.
    /// </summary>
    public static bool TryParseWire(string? value, out AppRole role)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "admin": role = AppRole.Admin; return true;
            case "secretariat": role = AppRole.Secretariat; return true;
            case "finance": role = AppRole.Finance; return true;
            case "executive": role = AppRole.Executive; return true;
            case "viewer": role = AppRole.Viewer; return true;
            default: role = default; return false;
        }
    }

    public static AppRole ParseWire(string? value) =>
        TryParseWire(value, out var role)
            ? role
            : throw new ArgumentException($"Unknown role '{value}'", nameof(value));

    public static IReadOnlyList<string> AllWireValues { get; } =
        Enum.GetValues<AppRole>().Select(r => r.ToWire()).ToArray();
}
