using Utsav.Infrastructure.Authentication;

namespace Utsav.UnitTests.Provisioning;

public class InviteCodesTests
{
    [Fact]
    public void A_generated_code_is_long_and_unique()
    {
        var codes = Enumerable.Range(0, 500).Select(_ => InviteCodes.Generate()).ToList();

        Assert.Equal(500, codes.Distinct().Count());
        // 128 bits of randomness, shown as 32 hex characters.
        Assert.All(codes, c => Assert.Equal(32, c.Length));
        Assert.All(codes, c => Assert.Matches("^[0-9A-F]{32}$", c));
    }

    [Fact]
    public void The_stored_value_is_a_hash_not_the_code()
    {
        var code = InviteCodes.Generate();

        var hash = InviteCodes.Hash(code);

        Assert.NotEqual(code, hash);
        Assert.DoesNotContain(code, hash, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Hashing_is_deterministic_and_distinguishes_codes()
    {
        var a = InviteCodes.Generate();
        var b = InviteCodes.Generate();

        Assert.Equal(InviteCodes.Hash(a), InviteCodes.Hash(a));
        Assert.NotEqual(InviteCodes.Hash(a), InviteCodes.Hash(b));
    }

    [Fact]
    public void A_code_pasted_with_spaces_dashes_or_lowercase_still_matches()
    {
        // People copy codes out of chat messages and emails.
        var code = InviteCodes.Generate();
        var mangled = " " + string.Join("-", Enumerable.Range(0, 4).Select(i => code.Substring(i * 8, 8))).ToLowerInvariant() + " ";

        Assert.Equal(InviteCodes.Hash(code), InviteCodes.Hash(mangled));
    }
}
