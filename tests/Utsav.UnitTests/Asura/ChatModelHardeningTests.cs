using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// Two failure modes that must degrade to a clean <see cref="ChatModelException"/>
/// rather than an unhandled exception that escapes <see cref="FallbackChatModel"/>'s
/// catch entirely.
/// </summary>
public class ChatModelHardeningTests
{
    /// <summary>
    /// A key copied from a terminal (<c>echo $KEY</c> rather than
    /// <c>printf '%s' "$KEY"</c>) commonly carries a trailing newline invisible
    /// in a portal text field. Left untrimmed it reaches
    /// <c>AuthenticationHeaderValue</c>'s constructor, which throws
    /// <see cref="FormatException"/> for an embedded control character —
    /// before any network call, and before the try/catch that turns a model
    /// failure into a <see cref="ChatModelException"/> used to begin.
    /// </summary>
    [Fact]
    public async Task A_key_with_an_embedded_newline_fails_as_a_clean_chat_model_exception()
    {
        var options = new AsuraProviderOptions
        {
            Provider = "groq",
            Model = "test-model",
            BaseUrl = "https://example.invalid/v1/chat/completions",
            ApiKey = "bad-key\n",
        };

        using var http = new HttpClient();
        var model = new OpenAiCompatibleChatModel(http, options, NullLogger<OpenAiCompatibleChatModel>.Instance);

        // Not FormatException escaping uncaught -- a ChatModelException the
        // fallback chain actually knows how to catch and move past.
        var ex = await Assert.ThrowsAsync<ChatModelException>(() =>
            model.CompleteAsync(new ChatRequest([new ChatMessage("user", "hi")]), CancellationToken.None));

        // Not transient: retrying the exact same malformed key fails
        // identically, so a retry would only waste 600ms for nothing.
        Assert.False(ex.IsTransient);
    }

    /// <summary>
    /// And the fallback chain must actually move past it to the next tier
    /// rather than the whole request dying on the first tier's construction
    /// failure.
    /// </summary>
    [Fact]
    public async Task The_fallback_chain_moves_past_a_tier_whose_key_is_malformed()
    {
        var broken = new AsuraProviderOptions
        {
            Provider = "groq", Model = "m", BaseUrl = "https://example.invalid", ApiKey = "bad\n",
        };
        using var http = new HttpClient();
        var brokenTier = new OpenAiCompatibleChatModel(http, broken, NullLogger<OpenAiCompatibleChatModel>.Instance);

        var working = new StubModel("fine/model", () => new ChatResult("ok", "fine", "stub", 1, 1));
        var chain = new FallbackChatModel([brokenTier, working], NullLogger<FallbackChatModel>.Instance);

        var result = await chain.CompleteAsync(new ChatRequest([new ChatMessage("user", "hi")]), CancellationToken.None);

        Assert.Equal("ok", result.Content);
        Assert.Equal(2, chain.LastTierUsed);
    }

    private sealed class StubModel(string name, Func<ChatResult> answer) : IChatModel
    {
        public string Name => name;
        public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct) =>
            Task.FromResult(answer());
    }

    [Theory]
    [InlineData("real-key\n", "real-key")]
    [InlineData("real-key\r\n", "real-key")]
    [InlineData("  real-key  ", "real-key")]
    [InlineData("real-key", "real-key")]
    public void Groq_and_nvidia_keys_are_trimmed_on_the_way_in(string raw, string expected)
    {
        var options = AsuraOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Asura:GroqApiKey"] = raw }).Build());

        Assert.Equal(expected, options.GroqApiKey);
        Assert.Equal(expected, options.Providers[0].ApiKey);
    }

    [Fact]
    public void A_per_tier_indexed_key_is_also_trimmed()
    {
        var options = AsuraOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Asura:Providers:0:ApiKey"] = "indexed-key\n" }).Build());

        Assert.Equal("indexed-key", options.Providers[0].ApiKey);
    }
}
