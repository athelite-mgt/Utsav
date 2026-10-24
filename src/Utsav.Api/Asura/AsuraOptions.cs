namespace Utsav.Api.Asura;

/// <summary>
/// Configuration for the Asura reporting assistant, bound from the
/// <c>Asura</c> section.
///
/// <para>
/// Nothing here carries a secret in source control. API keys arrive through
/// configuration only, which in production means Azure App Service application
/// settings (<c>Asura__Providers__0__ApiKey</c> and friends). Until those exist
/// the feature cannot talk to a model even if somebody flips
/// <see cref="Enabled"/> — a second, independent reason production stays
/// unaffected by this deploy.
/// </para>
/// </summary>
internal sealed class AsuraOptions
{
    public const string SectionName = "Asura";

    /// <summary>
    /// Master switch, <b>off by default</b>. With it off the endpoints are never
    /// mapped and the nav entry is absent, so the deployed app behaves exactly
    /// as it did before this feature existed. Turning it on is a configuration
    /// change and a restart, and turning it back off is the same — no redeploy
    /// either way.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether the assistant may write its own SQL for questions the reviewed
    /// report catalogue does not cover. <b>Off by default.</b>
    ///
    /// <para>
    /// With it off, an unmatched question gets an honest "I can't answer that
    /// yet, here are the reports I do have" instead of improvised SQL against
    /// the committee's live books. The four safety layers all still apply when
    /// it is on; this switch decides whether that surface exists at all, and
    /// the cautious default is that it does not until the fixed path has earned
    /// trust in real use.
    /// </para>
    /// </summary>
    public bool AllowFreeformSql { get; set; }

    /// <summary>
    /// The Groq API key, used by every Groq tier in the chain.
    ///
    /// <para>
    /// Flat rather than per-tier on purpose. Two tiers are Groq, so an indexed
    /// setting meant pasting the same key twice and getting it wrong once; and
    /// a key is a credential for a <i>provider</i>, not for a position in a
    /// fallback chain, so reordering the chain should not mean re-entering
    /// keys. In Azure this is one application setting:
    /// <c>Asura__GroqApiKey</c>.
    /// </para>
    /// </summary>
    public string GroqApiKey { get; set; } = "";

    /// <summary>The NVIDIA NIM API key. In Azure: <c>Asura__NvidiaApiKey</c>.</summary>
    public string NvidiaApiKey { get; set; } = "";

    /// <summary>Questions per user per hour. Stops a stuck client from draining a shared free-tier quota.</summary>
    public int MaxQuestionsPerUserPerHour { get; set; } = 30;

    /// <summary>Hard cap on rows returned to the model or the browser.</summary>
    public int MaxRows { get; set; } = 5_000;

    /// <summary>Per-query SQL timeout.</summary>
    public int QueryTimeoutSeconds { get; set; } = 20;

    /// <summary>
    /// Model tiers, tried in order. Defaults are the three that actually
    /// answered correctly when probed on 2026-10-05; the two models named as
    /// primary and fallback in the machine's CLAUDE.md were both already dead
    /// (HTTP 410 end-of-life and HTTP 404 respectively), so these are measured
    /// choices rather than documented ones.
    /// </summary>
    public List<AsuraProviderOptions> Providers { get; set; } = DefaultProviders();

    /// <summary>
    /// Builds options from configuration, correctly.
    ///
    /// <para>
    /// The subtlety this exists for: <c>IConfiguration.Bind</c> <b>appends</b>
    /// to a <c>List&lt;T&gt;</c> that already holds items instead of merging
    /// into it. Binding straight onto <see cref="DefaultProviders"/> turned
    /// three supplied <c>Providers:N:ApiKey</c> settings into six providers —
    /// three keyless defaults plus three entries holding only a key — so every
    /// request went to an empty base URL and production reported that no model
    /// was reachable while the keys were perfectly valid.
    /// </para>
    ///
    /// <para>
    /// So: bind with the list emptied, then fold what configuration supplied
    /// onto the defaults by index, then resolve each tier's key. Used by
    /// <see cref="AsuraRegistration.AddAsura"/> and by its tests, so the thing
    /// under test is the thing that runs.
    /// </para>
    /// </summary>
    public static AsuraOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        // Scalars only: Providers is emptied first so Bind's append-to-a-
        // non-empty-List behaviour cannot duplicate the defaults, and the
        // provider entries are then read explicitly below rather than bound.
        var options = new AsuraOptions { Providers = [] };
        section.Bind(options);
        options.Providers = DefaultProviders();

        // Trimmed here, once, at the point values enter the system. A key
        // pasted into the Azure Portal from a terminal (`echo $KEY` rather
        // than `printf '%s' "$KEY"`) very commonly carries a trailing
        // newline invisible in the portal's text field. Left untrimmed, that
        // single stray character makes AuthenticationHeaderValue's
        // constructor throw a FormatException — which, unlike a rejected
        // key, is not a ChatModelException and would escape the fallback
        // chain's catch entirely rather than producing a clean per-tier
        // failure message.
        options.GroqApiKey = options.GroqApiKey.Trim();
        options.NvidiaApiKey = options.NvidiaApiKey.Trim();

        // Read the provider entries by their LITERAL configuration index
        // instead of letting them bind into a list. Binding loses the index —
        // supplying only Providers:2:ApiKey arrives as a single element, so
        // anything positional would apply that key to tier 0 — whereas
        // GetChildren() hands back the real keys "0", "1", "2" and the
        // ambiguity disappears.
        var configured = new SortedDictionary<int, AsuraProviderOptions>();
        foreach (var child in section.GetSection(nameof(Providers)).GetChildren())
        {
            if (!int.TryParse(child.Key, out var index))
            {
                continue;
            }

            var entry = new AsuraProviderOptions();
            child.Bind(entry);
            entry.ApiKey = entry.ApiKey.Trim();
            configured[index] = entry;
        }

        // Two shapes are accepted, distinguished by whether any entry actually
        // describes a model. An entry naming a model and a base URL is somebody
        // specifying their own chain, so configuration wins outright. An entry
        // carrying only a key is the shape the original portal instructions
        // produced — those are key overrides for the measured chain, applied at
        // the index they were written at. Throwing them away and reporting
        // "no model configured" while the keys sat right there in the portal
        // was the previous behaviour, and it was wrong.
        var describesAChain = configured.Values.Any(p =>
            !string.IsNullOrWhiteSpace(p.Model) && !string.IsNullOrWhiteSpace(p.BaseUrl));

        if (describesAChain)
        {
            options.Providers = [.. configured.Values];
        }
        else
        {
            foreach (var (index, entry) in configured)
            {
                if (index < options.Providers.Count && !string.IsNullOrWhiteSpace(entry.ApiKey))
                {
                    options.Providers[index].ApiKey = entry.ApiKey;
                }
            }
        }

        options.ResolveKeys();
        return options;
    }

    private void ResolveKeys()
    {
        foreach (var provider in Providers)
        {
            provider.ApiKey = ResolveApiKey(provider);
        }
    }

    /// <summary>
    /// Whether a tier could actually issue a request. A tier missing a model or
    /// a base URL is worse than absent: it fails every call and still consumes
    /// a position in the fallback chain.
    /// </summary>
    public static bool IsUsable(AsuraProviderOptions provider) =>
        !string.IsNullOrWhiteSpace(provider.ApiKey)
        && !string.IsNullOrWhiteSpace(provider.Model)
        && !string.IsNullOrWhiteSpace(provider.BaseUrl);

    /// <summary>
    /// The prefix every key from this provider carries — a public format
    /// convention shared by every customer's key, not secret material, so
    /// reporting whether a configured key matches it reveals nothing about
    /// the key itself. Used by <c>/api/asura/diagnostics</c> to catch the
    /// failure mode behind a real production incident: both Groq and NVIDIA
    /// returned a clean <c>HTTP 401</c> — proof the request reached the
    /// provider and was authenticated, not that egress was blocked — pointing
    /// squarely at the value typed into the App Service setting rather than
    /// at the network. A key that doesn't start with its provider's prefix is
    /// almost certainly the wrong value, a stale credential, or one mangled in
    /// the portal's text field, and this makes that visible without anyone
    /// ever pasting the key to find out.
    /// </summary>
    public static string? ExpectedKeyPrefix(string provider) => provider.ToLowerInvariant() switch
    {
        "groq" => "gsk_",
        "nvidia" => "nvapi-",
        _ => null,
    };

    /// <summary>
    /// True when a key is present and, for a known provider, matches its
    /// published prefix. A provider this helper doesn't recognise returns
    /// true for any non-empty key — "known shape" is a useful positive
    /// signal, but the absence of a known shape to check is not itself
    /// evidence of a problem.
    /// </summary>
    public static bool LooksLikeValidKey(AsuraProviderOptions provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ApiKey))
        {
            return false;
        }

        var prefix = ExpectedKeyPrefix(provider.Provider);
        return prefix is null || provider.ApiKey.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The key a tier should use: its own override if set, otherwise the flat
    /// key for its provider. Unknown provider names resolve to empty, which
    /// drops the tier rather than sending a request with no credential.
    /// </summary>
    public string ResolveApiKey(AsuraProviderOptions provider) =>
        !string.IsNullOrWhiteSpace(provider.ApiKey) ? provider.ApiKey
        : provider.Provider.Equals("groq", StringComparison.OrdinalIgnoreCase) ? GroqApiKey
        : provider.Provider.Equals("nvidia", StringComparison.OrdinalIgnoreCase) ? NvidiaApiKey
        : "";

    public static List<AsuraProviderOptions> DefaultProviders() =>
    [
        // 1.9s, correct SQL, best speed/quality balance of everything probed.
        new()
        {
            Provider = "groq",
            Model = "openai/gpt-oss-120b",
            BaseUrl = "https://api.groq.com/openai/v1/chat/completions",
        },
        // 7.9s but the best SQL of the lot (it wrote a proper CTE to resolve the
        // active puja year). Different company and quota, so it survives the
        // Groq rate limit or outage that tier 1 cannot.
        new()
        {
            Provider = "nvidia",
            Model = "nvidia/nemotron-3-super-120b-a12b",
            BaseUrl = "https://integrate.api.nvidia.com/v1/chat/completions",
        },
        // 1.0s, separate Groq rate-limit bucket from tier 1.
        new()
        {
            Provider = "groq",
            Model = "openai/gpt-oss-20b",
            BaseUrl = "https://api.groq.com/openai/v1/chat/completions",
        },
    ];
}

internal sealed class AsuraProviderOptions
{
    public string Provider { get; set; } = "";

    public string Model { get; set; } = "";

    public string BaseUrl { get; set; } = "";

    /// <summary>
    /// Optional per-tier override. Normally left empty and resolved from
    /// <see cref="AsuraOptions.GroqApiKey"/> or
    /// <see cref="AsuraOptions.NvidiaApiKey"/> by
    /// <see cref="AsuraOptions.ResolveApiKey"/>. Never committed.
    /// </summary>
    public string ApiKey { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Sent on every request, and <b>mandatory</b> — not cosmetic.
    ///
    /// <para>
    /// .NET's <c>HttpClient</c> sends no User-Agent by default. Groq sits behind
    /// Cloudflare, which answers such requests with <c>HTTP 403, "error code:
    /// 1010"</c> on <c>/chat/completions</c> while still happily serving
    /// <c>GET /v1/models</c>. The result is a key that looks valid when you
    /// test it and fails on every real call — confirmed by hitting exactly that
    /// during the 2026-10-05 probe, where adding this header took the same
    /// request from 403 to 200 in 0.38s.
    /// </para>
    /// </summary>
    public string UserAgent { get; set; } = "Asura/1.0 (+Utsav)";
}

