using Utsav.Infrastructure.Authentication;

namespace Utsav.UnitTests.Authentication;

/// <summary>
/// Pins the properties the machine-credential scheme depends on: keys are
/// unguessable and unique, the stored hash is deterministic (so lookup is an
/// index seek) but one-way, and the displayed prefix can never be enough to
/// reconstruct the key.
/// </summary>
public class ApiKeyServiceTests
{
    [Fact]
    public void Generated_keys_carry_the_app_prefix_so_a_leak_is_identifiable()
    {
        Assert.StartsWith(ApiKeyService.KeyPrefix, ApiKeyService.GenerateKey());
    }

    [Fact]
    public void Every_generated_key_is_distinct()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => ApiKeyService.GenerateKey()).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void Generated_keys_carry_enough_entropy_to_resist_guessing()
    {
        // 32 random bytes, base64url — the body must not have been truncated
        // to something brute-forceable by whatever encodes it.
        var body = ApiKeyService.GenerateKey()[ApiKeyService.KeyPrefix.Length..];

        Assert.True(body.Length >= 40, $"key body was only {body.Length} chars");
    }

    [Fact]
    public void Hash_is_deterministic_so_lookup_can_be_an_index_seek()
    {
        var key = ApiKeyService.GenerateKey();

        Assert.Equal(ApiKeyService.HashKey(key), ApiKeyService.HashKey(key));
    }

    [Fact]
    public void Different_keys_hash_differently()
    {
        Assert.NotEqual(
            ApiKeyService.HashKey(ApiKeyService.GenerateKey()),
            ApiKeyService.HashKey(ApiKeyService.GenerateKey()));
    }

    [Fact]
    public void Hash_is_hex_sha256_matching_the_column_width()
    {
        var hash = ApiKeyService.HashKey(ApiKeyService.GenerateKey());

        // 64 hex chars — the api_keys.key_hash column is nvarchar(64), so a
        // wider digest would silently truncate and break every lookup.
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Hash_does_not_contain_the_key_itself()
    {
        var key = ApiKeyService.GenerateKey();

        Assert.DoesNotContain(key[ApiKeyService.KeyPrefix.Length..], ApiKeyService.HashKey(key));
    }

    [Fact]
    public void Display_prefix_is_a_short_non_reconstructable_slice()
    {
        var key = ApiKeyService.GenerateKey();

        var prefix = ApiKeyService.DisplayPrefixOf(key);

        Assert.StartsWith(prefix, key);
        Assert.True(prefix.Length < key.Length / 2, "prefix reveals too much of the key");
    }
}
