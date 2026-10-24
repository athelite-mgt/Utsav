using System.Security.Cryptography;
using System.Text;

namespace Utsav.Infrastructure.Authentication;

/// <summary>
/// One-time codes that hand a committee's first administrator their access.
///
/// A code is 128 bits of randomness, so it cannot be guessed, and only its
/// SHA-256 hash is stored: a leaked database yields nothing redeemable. It is
/// shown once, to the platform operator, who passes it to the real person out
/// of band.
///
/// This exists because the app has no email verification: anyone can create an
/// account under any address. Granting admin to "whoever signs up as
/// alice@example.org" would let a stranger who registered that address first
/// become the committee's administrator.
/// </summary>
public static class InviteCodes
{
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public static string Hash(string code)
    {
        // Pasted out of chat or email: tolerate spaces, dashes and case.
        var normalised = new string(code.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised))).ToLowerInvariant();
    }
}
