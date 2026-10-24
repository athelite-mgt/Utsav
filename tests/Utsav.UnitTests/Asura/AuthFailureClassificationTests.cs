using Microsoft.Extensions.Logging.Abstractions;
using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// Whether the fallback chain correctly recognises "every provider rejected
/// the key" as distinct from "every provider was unreachable".
///
/// <para>
/// This is the fix for a real incident: production reported "can't reach its
/// reasoning models" while every provider was in fact returning a clean
/// HTTP 401 — proof the request had arrived and been read, not that egress
/// was blocked. The wording was technically defensible (no answer came back
/// either way) and still sent the investigation toward the wrong cause. These
/// two situations need different messages, which means the code first needs
/// to tell them apart.
/// </para>
/// </summary>
public class AuthFailureClassificationTests
{
    private sealed class StubModel(string name, ChatModelException toThrow) : IChatModel
    {
        public string Name => name;
        public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct) =>
            throw toThrow;
    }

    private static ChatModelException AuthRejected(string name) =>
        new($"{name}: HTTP 401 — Invalid API Key") { IsAuthFailure = true };

    private static ChatModelException Unreachable(string name) =>
        new($"{name}: transport failure — Connection refused") { IsTransient = true };

    [Fact]
    public async Task All_tiers_rejecting_the_key_is_flagged_as_an_auth_problem()
    {
        var chain = new FallbackChatModel(
            [
                new StubModel("groq/a", AuthRejected("groq/a")),
                new StubModel("nvidia/b", AuthRejected("nvidia/b")),
                new StubModel("groq/c", AuthRejected("groq/c")),
            ],
            NullLogger<FallbackChatModel>.Instance);

        var ex = await Assert.ThrowsAsync<ChatModelException>(() =>
            chain.CompleteAsync(new ChatRequest([new ChatMessage("user", "hi")]), CancellationToken.None));

        Assert.True(ex.AllTiersRejectedAuth);
    }

    [Fact]
    public async Task A_mix_of_auth_rejection_and_network_failure_is_not_flagged_as_purely_an_auth_problem()
    {
        // If even one tier failed for a different reason, "every provider
        // rejected the key" would be an overclaim -- the honest fallback
        // wording ("can't reach") still applies.
        var chain = new FallbackChatModel(
            [
                new StubModel("groq/a", AuthRejected("groq/a")),
                new StubModel("nvidia/b", Unreachable("nvidia/b")),
            ],
            NullLogger<FallbackChatModel>.Instance);

        var ex = await Assert.ThrowsAsync<ChatModelException>(() =>
            chain.CompleteAsync(new ChatRequest([new ChatMessage("user", "hi")]), CancellationToken.None));

        Assert.False(ex.AllTiersRejectedAuth);
    }

    [Fact]
    public async Task All_tiers_failing_to_connect_is_not_flagged_as_an_auth_problem()
    {
        var chain = new FallbackChatModel(
            [
                new StubModel("groq/a", Unreachable("groq/a")),
                new StubModel("nvidia/b", Unreachable("nvidia/b")),
            ],
            NullLogger<FallbackChatModel>.Instance);

        var ex = await Assert.ThrowsAsync<ChatModelException>(() =>
            chain.CompleteAsync(new ChatRequest([new ChatMessage("user", "hi")]), CancellationToken.None));

        Assert.False(ex.AllTiersRejectedAuth);
    }

    [Theory]
    [InlineData("groq", "gsk_realkey12345", true)]
    [InlineData("groq", "nvapi-wrongprovider", false)]
    [InlineData("groq", "", false)]
    [InlineData("nvidia", "nvapi-realkey12345", true)]
    [InlineData("nvidia", "gsk_wrongprovider", false)]
    [InlineData("unknown-provider", "anything-at-all", true)]
    public void Key_format_check_catches_the_wrong_value_without_reading_it(
        string provider, string key, bool expectedValid)
    {
        var options = new AsuraProviderOptions { Provider = provider, ApiKey = key };
        Assert.Equal(expectedValid, AsuraOptions.LooksLikeValidKey(options));
    }

    [Fact]
    public void Expected_prefixes_match_each_providers_published_key_format()
    {
        Assert.Equal("gsk_", AsuraOptions.ExpectedKeyPrefix("groq"));
        Assert.Equal("nvapi-", AsuraOptions.ExpectedKeyPrefix("nvidia"));
        Assert.Null(AsuraOptions.ExpectedKeyPrefix("something-else"));
    }
}
