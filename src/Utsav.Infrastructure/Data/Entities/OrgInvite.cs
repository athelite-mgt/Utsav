namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// A one-time invitation that hands a committee's first administrator their
/// access. Only the hash of the code is stored (see InviteCodes), and the
/// invitation names the email address it is for: redeeming needs both the code
/// and an account registered under that address.
///
/// Like sessions, memberships and API keys, this is read BEFORE the caller has
/// any committee (that is the point of it), so it is exempt from the tenant
/// query filter and from row-level security, and is protected by the secrecy of
/// its code instead.
/// </summary>
public partial class OrgInvite
{
    public Guid Id { get; set; }

    public Guid OrgId { get; set; }

    /// <summary>The address the invitation is for, lower-cased.</summary>
    public string Email { get; set; } = null!;

    public string Role { get; set; } = null!;

    /// <summary>Lower-case hex SHA-256 of the normalised code. The code itself is never stored.</summary>
    public string CodeHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RedeemedAt { get; set; }

    public Guid? RedeemedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization Org { get; set; } = null!;
}
