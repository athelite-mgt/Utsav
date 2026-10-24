using System.Globalization;
using System.Text.RegularExpressions;

namespace Utsav.Domain.Finance;

/// <summary>
/// What to call next year's puja, when nobody has said.
///
/// The committee's own wording is carried forward: "Kali Puja 2026" rolls into
/// "Kali Puja 2027". The old behaviour wrote one festival's name for every
/// committee, which was simply wrong for any other festival. With no previous name there is
/// nothing to carry forward, so the answer is just the year: guessing a festival
/// is how that bug happened.
/// </summary>
public static partial class YearNaming
{
    // A four-digit year, on word boundaries so "Club 47" and "12345" are left alone.
    [GeneratedRegex(@"\b(?:19|20)\d{2}\b")]
    private static partial Regex YearToken();

    public static string Next(string? previousName, int year)
    {
        var name = previousName?.Trim() ?? "";
        var newYear = year.ToString(CultureInfo.InvariantCulture);

        if (name.Length == 0)
        {
            return newYear;
        }

        var matches = YearToken().Matches(name);
        if (matches.Count == 0)
        {
            return $"{name} {newYear}";
        }

        // The LAST year in the name: "Cup 2025 Final 2026" is the 2026 edition.
        var last = matches[^1];
        return string.Concat(name.AsSpan(0, last.Index), newYear, name.AsSpan(last.Index + last.Length));
    }
}
