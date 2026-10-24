using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// A long-lived credential for a machine caller (the committee's Hermes
/// agent fetching the audit report on a schedule), deliberately *not* a
/// human login.
///
/// Why a key rather than the agent posting a member's email and password:
/// a password is one credential for the whole web UI and every write route
/// behind it, it cannot be narrowed, rotating it locks a real person out,
/// and it would have to sit in plaintext in the agent's config. A key is
/// scoped read-only to the report routes, can be revoked on its own without
/// touching anyone's account, and is issued per agent so a leak is
/// traceable to one integration.
///
/// Only <see cref="KeyHash"/> is stored — the plaintext key is shown once,
/// at creation, and is unrecoverable afterwards. A plain SHA-256 is correct
/// here (unlike the scrypt used for passwords): the key is 256 bits of
/// CSPRNG output, so there is no low-entropy guess space for a slow KDF to
/// defend, and lookups happen on every agent request.
/// </summary>
public partial class ApiKey
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    /// <summary>Who/what this key was issued to, e.g. "Hermes agent". Shown in Settings and written to the audit trail.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Lowercase hex SHA-256 of the plaintext key. Never the key itself.</summary>
    public string KeyHash { get; set; } = null!;

    /// <summary>
    /// The first few characters of the key, kept in the clear purely so the
    /// Settings list can say which key is which ("pk_live_3f9a…") without
    /// being able to reconstruct it.
    /// </summary>
    public string Prefix { get; set; } = null!;

    /// <summary>The member who issued it — null if that account was later deleted.</summary>
    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Best-effort last-use stamp for spotting a key nobody is calling any more. Not written on every request; see ApiKeyAuthenticationHandler.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>Set when revoked; a revoked key authenticates nothing. Kept rather than deleted so the audit trail still resolves its name.</summary>
    public DateTime? RevokedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual User? CreatedByUser { get; set; }
}
