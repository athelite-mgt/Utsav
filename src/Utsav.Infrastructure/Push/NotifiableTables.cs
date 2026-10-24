using System.Collections.Frozen;

namespace Utsav.Infrastructure.Push;

/// <summary>
/// Which DynamicSql writes are worth a push at all, and what to call them —
/// money-movement tables only ("any transaction update", per the request
/// this shipped for). Master data and configuration (donors, vendors, zones,
/// collectors, lookups, budget_lines, offerings, puja_years, org_members)
/// never fire one; a push about a zone rename would just be noise nobody
/// asked for.
/// </summary>
public static class NotifiableTables
{
    public static readonly FrozenDictionary<string, (string Label, string Route)> Meta =
        new Dictionary<string, (string Label, string Route)>
        {
            ["subscriptions"] = ("Membership / Special Donation", "/membership"),
            ["feasts"] = ("Feast", "/feast"),
            ["feast_expenses"] = ("Feast Expenses", "/feast-expenses"),
            ["ad_bookings"] = ("Advertising", "/ads"),
            ["special_donations"] = ("Other Incomes", "/other-incomes"),
            ["offerings"] = ("Offerings", "/offerings"),
            ["expenses"] = ("Expenses", "/expenses"),
            ["advances"] = ("Cash Advances", "/advances"),
            ["contracts"] = ("Vendor Contracts", "/vendors"),
            ["contract_payments"] = ("Vendor Contracts", "/vendors"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// The table a notification's own <c>Url</c> points back at — lets the
    /// Notifications page correlate a logged notification to the audit entry
    /// that caused it (same table, same actor, close in time) without storing
    /// a second copy of the change detail on the notification row itself.
    /// "contracts" and "contract_payments" share a route (both live on the
    /// Vendors page); built with simple assignment rather than
    /// <c>ToDictionary</c> so that's a harmless overwrite instead of a
    /// startup-crashing duplicate-key exception. Which of the two wins isn't
    /// guaranteed (<see cref="Meta"/>'s own enumeration order isn't), but both
    /// resolve to a real audit table on the same page, so a correlation
    /// attempt either lands on real detail or — same as any other miss —
    /// just falls back to the notification's own title and body.
    /// </summary>
    public static readonly FrozenDictionary<string, string> TableForRoute = BuildTableForRoute();

    private static FrozenDictionary<string, string> BuildTableForRoute()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (table, meta) in Meta)
        {
            map[meta.Route] = table;
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
