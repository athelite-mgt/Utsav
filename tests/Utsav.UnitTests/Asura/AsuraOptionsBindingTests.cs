using Microsoft.Extensions.Configuration;
using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// How <c>Asura:Providers</c> actually binds from configuration.
///
/// <para>
/// This exists because of a production failure: with the API keys correctly
/// set as App Service application settings, every model tier failed and the
/// UI reported that no model was reachable. The suspicion is the .NET
/// configuration binder's treatment of a <c>List&lt;T&gt;</c> property that
/// already holds default items — so the behaviour is pinned here rather than
/// reasoned about.
/// </para>
/// </summary>
public class AsuraOptionsBindingTests
{
    /// <summary>
    /// Goes through <see cref="AsuraOptions.Load"/> — the same path startup
    /// takes. Calling IConfiguration.Bind directly here would test .NET's
    /// behaviour rather than ours, and would keep passing while production
    /// stayed broken.
    /// </summary>
    private static AsuraOptions Bind(Dictionary<string, string?> settings) =>
        AsuraOptions.Load(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    /// <summary>
    /// The regression that took production down. These are the settings Boss
    /// actually had in the portal: a key per tier index and nothing else.
    /// Bound naively they produced six providers — three keyless defaults plus
    /// three entries holding only a key — so every request went to an empty
    /// base URL while the keys were perfectly valid.
    /// </summary>
    /// <summary>
    /// The original portal instructions, with no flat keys at all. These keys
    /// must WORK, not be discarded: reporting "no model configured" while the
    /// keys sat in the portal is what sent Boss round this loop twice.
    /// </summary>
    [Fact]
    public void Indexed_key_only_settings_configure_the_chain_on_their_own()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Asura:Enabled"] = "true",
            ["Asura:Providers:0:ApiKey"] = "groq-key",
            ["Asura:Providers:1:ApiKey"] = "nvidia-key",
            ["Asura:Providers:2:ApiKey"] = "groq-key",
        });

        Assert.True(options.Enabled);
        Assert.Equal(3, options.Providers.Count);

        // Each key lands on the tier it was written for, and every tier is
        // usable — a key AND the model and base URL it needs.
        Assert.Equal("groq-key", options.Providers[0].ApiKey);
        Assert.Equal("nvidia-key", options.Providers[1].ApiKey);
        Assert.Equal("groq-key", options.Providers[2].ApiKey);
        Assert.All(options.Providers, p =>
        {
            Assert.True(AsuraOptions.IsUsable(p));
            Assert.StartsWith("https://", p.BaseUrl);
        });
    }

    /// <summary>Both shapes present at once — flat keys and stale indexed ones.</summary>
    [Fact]
    public void Indexed_keys_take_precedence_over_the_flat_ones_at_their_own_index()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Asura:GroqApiKey"] = "flat-groq",
            ["Asura:NvidiaApiKey"] = "flat-nvidia",
            ["Asura:Providers:0:ApiKey"] = "indexed-groq",
        });

        Assert.Equal(3, options.Providers.Count);
        Assert.Equal("indexed-groq", options.Providers[0].ApiKey);
        // Untouched tiers still resolve from the flat keys.
        Assert.Equal("flat-nvidia", options.Providers[1].ApiKey);
        Assert.Equal("flat-groq", options.Providers[2].ApiKey);
        Assert.All(options.Providers, p => Assert.True(AsuraOptions.IsUsable(p)));
    }

    [Fact]
    public void The_default_chain_is_the_measured_tier_order()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Asura:GroqApiKey"] = "g",
            ["Asura:NvidiaApiKey"] = "n",
        });

        // Groq first, then a different provider so the chain survives a Groq
        // outage, then a different Groq model with its own rate-limit bucket.
        Assert.Equal("groq", options.Providers[0].Provider);
        Assert.Equal("nvidia", options.Providers[1].Provider);
        Assert.Equal("groq", options.Providers[2].Provider);
        Assert.NotEqual(options.Providers[0].Model, options.Providers[2].Model);
    }

    /// <summary>The route Boss is now told to use: two settings, no indices.</summary>
    [Fact]
    public void Two_flat_provider_keys_configure_the_whole_chain()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Asura:Enabled"] = "true",
            ["Asura:GroqApiKey"] = "groq-key",
            ["Asura:NvidiaApiKey"] = "nvidia-key",
        });

        Assert.Equal(3, options.Providers.Count);
        Assert.Equal("groq-key", options.Providers[0].ApiKey);
        Assert.Equal("nvidia-key", options.Providers[1].ApiKey);
        // Both Groq tiers share the one Groq key — the reason it is flat.
        Assert.Equal("groq-key", options.Providers[2].ApiKey);
    }

    /// <summary>
    /// An indexed key on its own is ignored, and that is the intended
    /// contract rather than a shortcoming.
    ///
    /// <para>
    /// A sparse index does not survive list binding: <c>Providers:2:ApiKey</c>
    /// alone arrives as a single element, not as a third one, so honouring it
    /// positionally would put the key on tier 0. Rather than guess, a chain
    /// with no usable tier falls back to the measured defaults — which is also
    /// what lets the stale indexed settings already sitting in the portal stop
    /// breaking the app the moment the flat keys are added.
    /// </para>
    /// </summary>
    /// <summary>
    /// A sparse index lands on the tier it names.
    ///
    /// <para>
    /// This is the case that made list binding unusable: bound into a list,
    /// <c>Providers:2:ApiKey</c> alone arrives as a single element and anything
    /// positional applies it to tier 0. Reading the literal configuration keys
    /// instead means index 2 means tier 2.
    /// </para>
    /// </summary>
    [Fact]
    public void A_sparse_indexed_key_lands_on_the_tier_it_names()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Asura:GroqApiKey"] = "shared",
            ["Asura:Providers:2:ApiKey"] = "special",
        });

        Assert.Equal(3, options.Providers.Count);
        Assert.Equal("special", options.Providers[2].ApiKey);
        // Not tier 0 — that still resolves from the flat Groq key.
        Assert.Equal("shared", options.Providers[0].ApiKey);
    }

    [Fact]
    public void An_index_beyond_the_chain_is_ignored_rather_than_crashing()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Asura:GroqApiKey"] = "g",
            ["Asura:NvidiaApiKey"] = "n",
            ["Asura:Providers:9:ApiKey"] = "nowhere",
        });

        Assert.Equal(3, options.Providers.Count);
        Assert.DoesNotContain("nowhere", options.Providers.Select(p => p.ApiKey));
    }

    [Fact]
    public void Binding_nothing_leaves_the_defaults_intact_but_keyless()
    {
        var options = Bind([]);

        Assert.False(options.Enabled);
        Assert.Equal(3, options.Providers.Count);
        Assert.All(options.Providers, p => Assert.Equal("", p.ApiKey));
    }

    [Fact]
    public void A_fully_specified_provider_list_replaces_the_defaults_rather_than_adding_to_them()
    {
        // The escape hatch: somebody who wants a different chain entirely
        // should get exactly what they asked for, not their entry plus ours.
        var options = Bind(new Dictionary<string, string?>
        {
            ["Asura:Providers:0:Provider"] = "groq",
            ["Asura:Providers:0:Model"] = "some/other-model",
            ["Asura:Providers:0:BaseUrl"] = "https://example.invalid/v1/chat/completions",
            ["Asura:Providers:0:ApiKey"] = "k",
        });

        Assert.Single(options.Providers);
        Assert.Equal("some/other-model", options.Providers[0].Model);
    }
}
