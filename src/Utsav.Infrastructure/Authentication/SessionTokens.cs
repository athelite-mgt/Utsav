using System.Security.Cryptography;
using System.Text;

namespace Utsav.Infrastructure.Authentication;

/// <summary>
/// How a session credential is made, stored and checked.
///
/// The browser holds the RAW token, in an httpOnly cookie no script can read.
/// The database holds only its SHA-256 hash, so a leaked database (a backup, a
/// read-only SQL injection, a support export) yields nothing that can be
/// presented as a live session. 256 random bits need no salt or stretching:
/// there is no password-shaped weakness for them to defend against.
///
/// The CSRF token is a second, separate hash of the same raw token. It is safe
/// to give to page script (it cannot be reversed into the session), it cannot
/// be forged or planted by another site or a sibling subdomain (it is not a
/// free-standing value the server merely compares two copies of), and it needs
/// no extra storage because the server can always recompute it.
/// </summary>
public static class SessionTokens
{
    public static string Generate() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    public static string CsrfFor(string raw) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("utsav-csrf:" + raw)));

    /// <summary>Constant-time, so response timing reveals nothing about how much of a guess was right.</summary>
    public static bool CsrfMatches(string raw, string? presented) =>
        presented is { Length: 64 }
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(CsrfFor(raw)), Encoding.ASCII.GetBytes(presented));
}
