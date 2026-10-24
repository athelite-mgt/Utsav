using System.Security.Cryptography;
using System.Text;
using Norgerman.Cryptography.Scrypt;

namespace Utsav.Domain.Authentication;

/// <summary>
/// Ported from <c>server/auth.mjs</c> (<c>hashPassword</c>/<c>verifyPassword</c>) so
/// this API can verify passwords against the accounts that already exist in
/// production — those hashes cannot be regenerated without the plaintext.
///
/// Storage format: <c>"{32-char hex salt}:{128-char hex hash}"</c>.
///
/// Non-obvious, load-bearing detail: Node generates the salt as a hex
/// *string* (<c>randomBytes(16).toString("hex")</c>) and then passes that
/// string straight into <c>scryptSync(password, salt, 64)</c> without
/// decoding it back to 16 raw bytes. Node encodes string arguments as UTF-8,
/// so the actual scrypt salt is the 32 ASCII bytes of the hex text itself,
/// not the 16 bytes it represents. Hex-decoding the salt here — the "more
/// correct-looking" thing to do — would silently produce a hasher that
/// cannot verify a single existing password. Confirmed against real Node
/// output (see ScryptPasswordHasherTests), not just read off the source.
///
/// Cost parameters (N=16384, r=8, p=1) are scryptSync's documented defaults,
/// used as-is since Node's call site never overrides them.
/// </summary>
public static class ScryptPasswordHasher
{
    private const int Cost = 16384;
    private const int BlockSize = 8;
    private const int Parallel = 1;
    private const int DerivedKeyLength = 64;

    public static string Hash(string password)
    {
        var saltHex = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var hashHex = Convert.ToHexStringLower(DeriveKey(password, saltHex));
        return $"{saltHex}:{hashHex}";
    }

    /// <summary>Google-provisioned users have no local password — <paramref name="stored"/> is null.</summary>
    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        var parts = stored.Split(':', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        var (saltHex, expectedHex) = (parts[0], parts[1]);
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        var candidate = DeriveKey(password, saltHex);
        return candidate.Length == expected.Length && CryptographicOperations.FixedTimeEquals(candidate, expected);
    }

    private static byte[] DeriveKey(string password, string saltHex) =>
        ScryptUtil.Scrypt(
            password: Encoding.UTF8.GetBytes(password),
            salt: Encoding.UTF8.GetBytes(saltHex),
            N: Cost,
            r: BlockSize,
            p: Parallel,
            dkLen: DerivedKeyLength);
}
