using System.Text.RegularExpressions;

namespace Utsav.Api.Endpoints;

/// <summary>
/// An accent colour is written into a CSS custom property in every member's
/// browser, so it is untrusted input reaching a stylesheet. It is validated as
/// a strict hex literal rather than sanitised or escaped: the set of legitimate
/// values is tiny, and an allowlist leaves no bypass to find.
/// </summary>
public static partial class BrandingValidation
{
    // \z, not $: $ also matches before a trailing newline, which would let
    // "#fff\n" through.
    [GeneratedRegex(@"\A#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})\z")]
    private static partial Regex HexColour();

    /// <summary>Null means "no brand colour" and falls back to the default theme.</summary>
    public static bool IsValidAccentColour(string? value) => value is null || HexColour().IsMatch(value);

    public static string? NormaliseAccentColour(string? value) => value?.ToLowerInvariant();
}
