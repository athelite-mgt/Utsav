using System.Collections.Concurrent;

namespace Utsav.Api.Asura;

/// <summary>
/// Wires Asura into the app — and, when it is switched off, wires nothing.
/// </summary>
internal static class AsuraRegistration
{
    /// <summary>
    /// Registers Asura's services only when <c>Asura:Enabled</c> is true.
    ///
    /// <para>
    /// Nothing is registered when it is off, which is stronger than registering
    /// services that refuse to run: there is no code path to reach, so the
    /// deployed app behaves exactly as it did before this feature existed. That
    /// is the property that makes shipping this to production safe while the
    /// feature is still new.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAsura(
        this IServiceCollection services, IConfiguration configuration, ILogger? log = null)
    {
        // Load, not Bind — see AsuraOptions.Load for why binding directly is
        // the bug that took production down.
        var options = AsuraOptions.Load(configuration);

        if (!options.Enabled)
        {
            return services;
        }

        // Keep only the tiers that could actually issue a request. A tier
        // missing a model or a base URL is worse than absent: it fails every
        // call and consumes a position in the fallback chain.
        var usable = new List<AsuraProviderOptions>();
        foreach (var provider in options.Providers)
        {
            if (AsuraOptions.IsUsable(provider))
            {
                usable.Add(provider);
                continue;
            }

            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(provider.ApiKey)) missing.Add("API key");
            if (string.IsNullOrWhiteSpace(provider.Model)) missing.Add("model");
            if (string.IsNullOrWhiteSpace(provider.BaseUrl)) missing.Add("base URL");

            // Logged by name, never by value — this line exists so the next
            // misconfiguration is one glance at the log, not an afternoon.
            log?.LogWarning(
                "Asura: ignoring provider tier '{Provider}/{Model}' — missing {Missing}. "
                + "Set Asura__GroqApiKey and Asura__NvidiaApiKey in application settings.",
                provider.Provider, provider.Model, string.Join(" and ", missing));
        }

        if (usable.Count == 0)
        {
            log?.LogError(
                "Asura is enabled but no model tier is usable. Set Asura__GroqApiKey and "
                + "Asura__NvidiaApiKey in the App Service application settings.");
        }
        else
        {
            log?.LogInformation("Asura enabled with {Count} model tier(s): {Tiers}",
                usable.Count, string.Join(" → ", usable.Select(p => $"{p.Provider}/{p.Model}")));
        }

        services.AddSingleton(options);
        services.AddSingleton<AsuraRateLimiter>();

        // Unconditional, and that word is the whole point of this line.
        //
        // The named clients below are registered one per *usable* tier, so
        // with an unusable chain that loop never runs — and AddHttpClient is
        // also what registers IHttpClientFactory itself. The IChatModel
        // factory asks for that factory, so resolution threw while the
        // endpoint's parameters were being built, before any handler code
        // could catch it. The result was that a misconfigured Asura answered
        // every question with a bare HTTP 500 rather than saying which
        // settings were missing. A misconfiguration must degrade to a readable
        // message, never to a stack trace the person asking cannot see.
        services.AddHttpClient();

        for (var i = 0; i < usable.Count; i++)
        {
            var provider = usable[i];
            services.AddHttpClient(HttpClientName(i), client =>
            {
                client.Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds);
                // Mandatory, not cosmetic — Groq's Cloudflare answers a
                // User-Agent-less request with 403/1010 while GET /v1/models
                // keeps working, which looks exactly like a bad key.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(provider.UserAgent);
            });
        }

        services.AddScoped<IChatModel>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var tiers = usable
                .Select((p, i) => (IChatModel)new OpenAiCompatibleChatModel(
                    factory.CreateClient(HttpClientName(i)),
                    p,
                    sp.GetRequiredService<ILogger<OpenAiCompatibleChatModel>>()))
                .ToList();

            // Scoped, not singleton: FallbackChatModel records which tier
            // answered so the UI can show it, and that is per-request state.
            return new FallbackChatModel(tiers, sp.GetRequiredService<ILogger<FallbackChatModel>>());
        });

        services.AddScoped<ReadOnlyQueryExecutor>();
        services.AddScoped<AsuraOrchestrator>();

        return services;
    }

    private static string HttpClientName(int tier) => $"asura-tier-{tier}";
}

/// <summary>
/// A question budget per member per hour. The Groq free tier is shared with the
/// Hermes agent on this machine, so a client stuck in a retry loop could drain
/// a quota that other things depend on; this bounds the damage to one member's
/// allowance.
/// </summary>
internal sealed class AsuraRateLimiter(AsuraOptions options)
{
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _asked = new();

    /// <summary>
    /// Records a question and says whether it is within budget. Returns false
    /// without recording when the member is already at the limit, so being
    /// rate-limited cannot itself push the window further out.
    /// </summary>
    public bool TryRecord(Guid userId, out int retryAfterSeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromHours(1);
        retryAfterSeconds = 0;

        var queue = _asked.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= options.MaxQuestionsPerUserPerHour)
            {
                retryAfterSeconds = (int)Math.Ceiling((window - (now - queue.Peek())).TotalSeconds);
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
