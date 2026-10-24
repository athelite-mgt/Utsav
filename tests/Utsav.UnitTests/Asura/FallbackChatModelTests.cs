using Microsoft.Extensions.Logging.Abstractions;
using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

public class FallbackChatModelTests
{
    /// <summary>A tier that either answers or fails, and records whether it was asked.</summary>
    private sealed class StubModel(string name, Func<ChatResult>? answer) : IChatModel
    {
        public string Name => name;

        public int Calls { get; private set; }

        public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            Calls++;
            return answer is null
                ? throw new ChatModelException($"{name} is down")
                : Task.FromResult(answer());
        }
    }

    private static StubModel Working(string name) =>
        new(name, () => new ChatResult("answer", name, "stub", 10, 5));

    private static StubModel Broken(string name) => new(name, null);

    private static FallbackChatModel Chain(params IChatModel[] tiers) =>
        new(tiers, NullLogger<FallbackChatModel>.Instance);

    private static ChatRequest AnyRequest() =>
        new([new ChatMessage("user", "hello")]);

    [Fact]
    public async Task Uses_the_first_tier_and_does_not_disturb_the_rest()
    {
        var first = Working("tier1");
        var second = Working("tier2");

        var result = await Chain(first, second).CompleteAsync(AnyRequest(), default);

        Assert.Equal("tier1", result.Model);
        Assert.Equal(1, first.Calls);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task Falls_through_to_the_next_provider_when_the_first_fails()
    {
        var first = Broken("groq");
        var second = Working("nvidia");

        var chain = Chain(first, second);
        var result = await chain.CompleteAsync(AnyRequest(), default);

        Assert.Equal("nvidia", result.Model);
        Assert.Equal(2, chain.LastTierUsed);
    }

    [Fact]
    public async Task Keeps_falling_through_to_the_last_tier()
    {
        var third = Working("tier3");
        var chain = Chain(Broken("tier1"), Broken("tier2"), third);

        var result = await chain.CompleteAsync(AnyRequest(), default);

        Assert.Equal("tier3", result.Model);
        Assert.Equal(3, chain.LastTierUsed);
        Assert.Equal(1, third.Calls);
    }

    [Fact]
    public async Task Reports_every_failure_when_the_whole_chain_is_exhausted()
    {
        var chain = Chain(Broken("groq"), Broken("nvidia"));

        var ex = await Assert.ThrowsAsync<ChatModelException>(
            () => chain.CompleteAsync(AnyRequest(), default));

        // Both names must appear: being told only about the last tier sends you
        // debugging the wrong provider.
        Assert.Contains("groq is down", ex.Message);
        Assert.Contains("nvidia is down", ex.Message);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_trigger_a_fallback()
    {
        // The distinction that matters operationally: a user closing the tab
        // must not cause every remaining provider to be called. Absorbing
        // cancellation as "tier failed" would turn one abandoned request into
        // three real ones against a shared free-tier quota.
        var second = Working("tier2");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Chain(Working("tier1"), second).CompleteAsync(AnyRequest(), cts.Token));

        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task An_empty_chain_says_so_rather_than_failing_obscurely()
    {
        var ex = await Assert.ThrowsAsync<ChatModelException>(
            () => Chain().CompleteAsync(AnyRequest(), default));

        // The message has to name the settings to add. "No models configured"
        // was true but told whoever read it nothing about what to do.
        Assert.Contains("no model configured", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Asura__GroqApiKey", ex.Message);
        // Flagged as configuration so the orchestrator shows it verbatim
        // rather than replacing it with a generic outage message.
        Assert.True(ex.IsConfiguration);
    }
}
