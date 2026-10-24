using Utsav.Infrastructure.Authentication;

namespace Utsav.UnitTests.Authentication;

public class SessionTokensTests
{
    [Fact]
    public void A_generated_token_is_long_unique_and_unguessable_in_shape()
    {
        var tokens = Enumerable.Range(0, 500).Select(_ => SessionTokens.Generate()).ToList();

        Assert.Equal(500, tokens.Distinct().Count());
        // 256 bits of randomness as 64 hex characters.
        Assert.All(tokens, t => Assert.Matches("^[0-9a-f]{64}$", t));
    }

    [Fact]
    public void What_is_stored_is_a_hash_so_a_leaked_database_holds_no_usable_session()
    {
        var raw = SessionTokens.Generate();

        var stored = SessionTokens.Hash(raw);

        Assert.NotEqual(raw, stored);
        Assert.Matches("^[0-9a-f]{64}$", stored);
        Assert.Equal(stored, SessionTokens.Hash(raw));
        // Presenting the stored value as if it were the token must not work.
        Assert.NotEqual(stored, SessionTokens.Hash(stored));
    }

    [Fact]
    public void The_csrf_token_is_derived_from_the_session_and_cannot_be_turned_back_into_it()
    {
        var raw = SessionTokens.Generate();

        var csrf = SessionTokens.CsrfFor(raw);

        Assert.Equal(csrf, SessionTokens.CsrfFor(raw));
        Assert.Matches("^[0-9a-f]{64}$", csrf);
        Assert.NotEqual(raw, csrf);
        // Not the stored hash either: one must not unlock the other.
        Assert.NotEqual(SessionTokens.Hash(raw), csrf);
    }

    [Fact]
    public void Each_session_has_its_own_csrf_token()
    {
        Assert.NotEqual(SessionTokens.CsrfFor(SessionTokens.Generate()), SessionTokens.CsrfFor(SessionTokens.Generate()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Csrf_comparison_accepts_only_the_exact_value(bool correct)
    {
        var raw = SessionTokens.Generate();
        var presented = correct ? SessionTokens.CsrfFor(raw) : SessionTokens.CsrfFor(raw)[..^1] + "0";

        // (the "wrong" value differs in its last character, unless the real one
        // already ends in 0, in which case flip it)
        if (!correct && presented == SessionTokens.CsrfFor(raw))
        {
            presented = SessionTokens.CsrfFor(raw)[..^1] + "1";
        }

        Assert.Equal(correct, SessionTokens.CsrfMatches(raw, presented));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void A_missing_or_malformed_csrf_value_never_matches(string? presented)
    {
        Assert.False(SessionTokens.CsrfMatches(SessionTokens.Generate(), presented));
    }
}
