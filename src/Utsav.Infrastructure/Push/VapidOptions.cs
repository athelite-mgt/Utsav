namespace Utsav.Infrastructure.Push;

/// <summary>
/// Bound from the <c>Push</c> config section — Subject is a plain app
/// setting (an identifying mailto:/https: URL, not a secret), while the two
/// keys are secrets: local dev via <c>dotnet user-secrets</c>, production via
/// Azure App Service's <c>Push__VapidPublicKey</c> / <c>Push__VapidPrivateKey</c>
/// application settings — same double-underscore convention already used for
/// <c>Cosmos__Key</c> etc.
///
/// Deliberately optional at startup, unlike Cosmos: a missing audit store
/// means the app can't do its core job, but a missing push config just means
/// nobody gets notified yet. <see cref="IsConfigured"/> lets every caller
/// degrade to a no-op instead of the app refusing to start over a feature
/// this genuinely can ship without.
/// </summary>
public sealed class VapidOptions
{
    public const string SectionName = "Push";

    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }
    public string? Subject { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(PrivateKey) &&
        !string.IsNullOrWhiteSpace(Subject);
}
