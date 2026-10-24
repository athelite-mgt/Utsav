using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Utsav.Api.Asura;

/// <summary>
/// Picks a report from a question without using a language model.
///
/// <para>
/// This is Asura's last line of defence, and the reason it is worth having is
/// a point about where the intelligence actually lives: the reports are
/// hand-written deterministic SQL, and the totals are computed by the database.
/// A model is needed only to <i>choose</i> a report and to <i>narrate</i> the
/// result. Neither is load-bearing. So when every provider is unreachable —
/// an outage, a rate limit, blocked egress from the host — Asura can still
/// match the question by keyword, run the right report, and show the figures,
/// the chart, the provenance and the exports. Only the prose paragraph is lost.
/// </para>
///
/// <para>
/// Deliberately conservative. A wrong report presented confidently is worse
/// than an honest "I'm not sure which report you meant", so a match needs a
/// real margin over the runner-up and the result is always labelled as having
/// been matched without a model.
/// </para>
/// </summary>
internal static partial class KeywordToolMatcher
{
    /// <summary>
    /// Trigger words per report, written by hand rather than derived from the
    /// titles. Derived keywords sound reasonable and match badly: "collection"
    /// appears in half the titles, so it has to be weighted by what actually
    /// distinguishes one report from another.
    /// </summary>
    private static readonly FrozenDictionary<string, string[]> Triggers =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["collections_by_zone"] =
                ["zone", "zones", "area", "areas", "locality", "para", "behind", "lagging", "worst"],
            ["collections_by_collector"] =
                ["collector", "collectors", "volunteer", "volunteers", "performance", "who collected"],
            ["donor_dues"] =
                ["due", "dues", "outstanding", "unpaid", "pending", "owe", "owes", "owing",
                 "not paid", "hasn't paid", "havent paid", "defaulter", "defaulters", "receivable"],
            ["budget_vs_actual"] =
                ["budget", "forecast", "over budget", "overspend", "overspent", "versus", "vs",
                 "against budget", "utilised", "utilized"],
            ["expenses_by_category"] =
                ["expense", "expenses", "spend", "spending", "spent", "cost", "costs", "category", "categories"],
            ["vendor_spend"] =
                ["vendor", "vendors", "supplier", "suppliers", "contractor", "contractors", "paid to"],
            ["advances_outstanding"] =
                ["advance", "advances", "imprest", "unsettled", "settle", "settled", "cash given"],
            ["income_mix"] =
                ["income", "revenue", "where the money", "split", "mix", "breakdown of income",
                 "sources", "source of funds"],
            ["monthly_collection_trend"] =
                ["trend", "month", "monthly", "over time", "by month", "timeline", "progress over"],
            ["year_summary"] =
                ["summary", "overall", "total", "totals", "position", "at a glance", "balance",
                 "how are we doing", "overview"],
            ["zone_donor_detail"] = ["donors in", "donor list for zone"],
            ["collector_donor_detail"] = ["donors for collector", "collector's donors"],
            ["category_expense_detail"] = ["bills in", "bills for category"],
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Triggers put through the same normalisation as the question.
    ///
    /// <para>
    /// Normalising only one side is a silent bug: the normaliser turns
    /// "hasn't" into "hasn t", so the literal trigger "hasn't paid" could
    /// never match the very question it was written for. Both sides go through
    /// <see cref="Normalize"/>, once, at startup.
    /// </para>
    /// </summary>
    private static readonly FrozenDictionary<string, string[]> NormalizedTriggers =
        Triggers.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(Normalize).Distinct(StringComparer.Ordinal).ToArray(),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Lower-cased, punctuation collapsed to single spaces, untrimmed at the edges.</summary>
    private static string Normalize(string text) =>
        Whitespace().Replace(Punctuation().Replace(text.ToLowerInvariant(), " "), " ").Trim();

    /// <summary>
    /// The best-matching report, or null when nothing matches clearly enough.
    ///
    /// <para>
    /// Scoring favours longer phrases: matching "not paid" is far stronger
    /// evidence than matching "paid", so a hit scores by its own word count.
    /// A winner must beat the runner-up outright, so a question that reads
    /// equally like two reports yields null rather than a coin toss.
    /// </para>
    /// </summary>
    public static string? Match(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        // Padded with spaces so a whole-word check is a plain Contains on
        // " word " — which is also why "zones?" still matches "zones".
        var haystack = $" {Normalize(question)} ";

        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (toolId, triggers) in NormalizedTriggers)
        {
            var score = 0;
            foreach (var trigger in triggers)
            {
                if (haystack.Contains($" {trigger} ", StringComparison.Ordinal))
                {
                    // Longer phrases are stronger signals than single words.
                    score += trigger.Count(c => c == ' ') + 1;
                }
            }

            if (score > 0)
            {
                scores[toolId] = score;
            }
        }

        if (scores.Count == 0)
        {
            return null;
        }

        var ranked = scores.OrderByDescending(kv => kv.Value).ToList();
        var best = ranked[0];

        // The detail reports need a parameter the question cannot supply (a
        // zone id, a collector id), so they are never the keyword answer —
        // they are only ever reached by clicking a row.
        if (ToolCatalogue.TryGet(best.Key, out var tool) && tool.Parameters.Any(p => p.Required))
        {
            return null;
        }

        // A tie is a question that reads equally like two different reports.
        // Guessing would present the wrong figures as though they answered what
        // was asked, which is the one outcome worth refusing outright.
        if (ranked.Count > 1 && ranked[1].Value >= best.Value)
        {
            return null;
        }

        return best.Key;
    }

    [GeneratedRegex(@"[^a-z0-9₹%]+")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
