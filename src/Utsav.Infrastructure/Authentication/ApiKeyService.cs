using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Authentication;

/// <summary>
/// Issues and verifies the machine credentials behind <see cref="ApiKey"/>.
/// See that entity for why this is a key rather than a reusable human login.
/// </summary>
public sealed class ApiKeyService(UtsavDbContext db)
{
    /// <summary>
    /// Marks the token as this app's, so a leaked key is identifiable on
    /// sight (in a log, a paste, a secret scanner) rather than looking like
    /// any other opaque blob.
    /// </summary>
    public const string KeyPrefix = "puja_sk_";

    /// <summary>How much of the key Settings may show to tell two keys apart.</summary>
    private const int DisplayPrefixLength = 16;

    /// <summary>
    /// 32 bytes of CSPRNG output, base64url-encoded. Returned in plaintext
    /// exactly once — only its hash is persisted, so a lost key is reissued,
    /// never recovered.
    /// </summary>
    public static string GenerateKey() =>
        KeyPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Plain SHA-256, deliberately: the input is 256 bits of random, so
    /// there is no guessable space for a slow KDF (scrypt, as used for
    /// passwords) to protect, and this runs on every agent request.
    /// </summary>
    public static string HashKey(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static string DisplayPrefixOf(string key) =>
        key.Length <= DisplayPrefixLength ? key : key[..DisplayPrefixLength];

    /// <summary>
    /// Resolves a presented key to a live record, or null. Revoked keys and
    /// unknown hashes are both null — the caller must not be told which.
    /// </summary>
    public async Task<ApiKey?> ResolveAsync(string presentedKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(presentedKey)) return null;

        var hash = HashKey(presentedKey);

        // A key is looked up by its high-entropy hash BEFORE any tenant is
        // known - the key is what establishes the tenant - so the tenant
        // filter must not apply here. This is the one deliberate cross-tenant
        // read on the authentication path.
        return await db.ApiKeys.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null, ct);
    }

    /// <summary>
    /// Best-effort "this key is still in use" stamp. Deliberately swallows
    /// failures and is never awaited on the request's critical path — a
    /// bookkeeping write must not be able to fail an otherwise-good report
    /// fetch.
    /// </summary>
    public async Task TouchAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await db.ApiKeys.Where(k => k.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow), ct);
        }
        catch
        {
            // Ignored on purpose — see the summary.
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
