using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Utsav.Api.Asura;
using Utsav.Infrastructure.Data;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// What the DI container actually produces, for the three configurations that
/// matter: off, on-but-unconfigured, and on-and-configured.
///
/// <para>
/// This exists because of a production HTTP 500. With Asura enabled but no
/// usable model tier, the per-tier <c>AddHttpClient</c> loop never ran — and
/// that call is also what registers <see cref="IHttpClientFactory"/>. The
/// <see cref="IChatModel"/> factory asks for it, so resolution threw while the
/// endpoint's parameters were being constructed, before any handler code could
/// turn it into a message. Every question returned a bare 500.
/// </para>
///
/// <para>
/// A unit test of the options alone could never have caught that: the options
/// were correct. Only building the container shows it.
/// </para>
/// </summary>
public class AsuraRegistrationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();

        // The executor needs a DbContext; it is never opened in these tests.
        services.AddDbContext<UtsavDbContext>(o => o.UseSqlServer("Server=unused;Database=unused;"));

        services.AddAsura(configuration);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The exact regression: enabled, no keys, and asking for the model must not throw on resolution.</summary>
    [Fact]
    public void An_enabled_but_unconfigured_asura_still_resolves_its_model_service()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Asura:Enabled"] = "true",
            // deliberately no keys at all
        });

        using var scope = provider.CreateScope();

        // Must resolve. Before the fix this threw InvalidOperationException
        // ("No service for type IHttpClientFactory") and surfaced as HTTP 500.
        var model = scope.ServiceProvider.GetRequiredService<IChatModel>();
        Assert.NotNull(model);
    }

    /// <summary>
    /// And having resolved, it must fail with an explanation naming the
    /// settings to add — the whole point of not letting it 500.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_chain_fails_with_a_message_that_says_what_to_set()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Asura:Enabled"] = "true" });
        using var scope = provider.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<IChatModel>();

        var ex = await Assert.ThrowsAsync<ChatModelException>(() =>
            model.CompleteAsync(new ChatRequest([new ChatMessage("user", "hi")]), CancellationToken.None));

        Assert.Contains("Asura__GroqApiKey", ex.Message);
    }

    [Fact]
    public void The_orchestrator_and_executor_resolve_when_asura_is_enabled()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Asura:Enabled"] = "true",
            ["Asura:GroqApiKey"] = "g",
            ["Asura:NvidiaApiKey"] = "n",
        });

        using var scope = provider.CreateScope();

        // Everything the /ask endpoint takes as a parameter, since a failure to
        // resolve any of them is another 500 before the handler runs.
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AsuraOrchestrator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ReadOnlyQueryExecutor>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AsuraRateLimiter>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AsuraOptions>());
    }

    [Fact]
    public void Nothing_at_all_is_registered_when_asura_is_disabled()
    {
        using var provider = Build([]);
        using var scope = provider.CreateScope();

        // The property that makes this safe to ship switched off: no service to
        // resolve, which is why MapAsuraEndpoints maps no routes either.
        Assert.Null(scope.ServiceProvider.GetService<AsuraOptions>());
        Assert.Null(scope.ServiceProvider.GetService<AsuraOrchestrator>());
        Assert.Null(scope.ServiceProvider.GetService<IChatModel>());
    }
}
