using System.Text;

namespace Utsav.Domain.Tenancy;

/// <summary>
/// A committee's short name: lower case, letters and digits, joined by single
/// hyphens. It is what a member types to ask to join a committee, so it has to
/// be predictable.
///
/// Unicode letters are kept deliberately. These committees' names are often
/// Bengali, and stripping to ASCII would collapse a name like "বারাকপুর কালী পূজা"
/// to nothing.
/// </summary>
public static class Slug
{
    public static string From(string name)
    {
        var slug = new StringBuilder(name.Length);
        var pendingSeparator = false;

        foreach (var c in name.Trim().ToLowerInvariant())
        {
            // Bengali vowel signs and the virama are combining marks, not
            // letters; dropping them would mangle every conjunct (পূজা).
            if (char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) is
                    System.Globalization.UnicodeCategory.NonSpacingMark or
                    System.Globalization.UnicodeCategory.SpacingCombiningMark)
            {
                if (pendingSeparator && slug.Length > 0)
                {
                    slug.Append('-');
                }

                pendingSeparator = false;
                slug.Append(c);
            }
            else
            {
                pendingSeparator = true;
            }
        }

        if (slug.Length == 0)
        {
            throw new ArgumentException("A name needs at least one letter or digit to make a slug from.", nameof(name));
        }

        return slug.ToString();
    }
}
