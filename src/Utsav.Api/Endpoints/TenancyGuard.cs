using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Thrown when runtime SQL would touch an org-scoped table without the
/// tenant predicate that keeps it inside one committee. A bug, not a user
/// error: it means a statement was written in a way the executor cannot
/// prove is tenant-safe.
/// </summary>
public sealed class TenancyViolationException(string message) : InvalidOperationException(message);

/// <summary>
/// Proves that a runtime-assembled statement is scoped to one committee.
///
/// This exists because the single-tenant design made tenant scoping the
/// *caller's* job: every endpoint hand-wrote <c>org_id=@org</c> and passed the
/// constant. Forgetting it compiled and ran, and would return another
/// committee's rows. Here the executor supplies <c>@org</c> and refuses any
/// statement that does not constrain on it, so the failure mode is a loud
/// exception in development rather than a silent leak in production.
///
/// Belt and braces, not the only defence: EF global query filters cover the
/// LINQ paths and Azure SQL row-level security covers everything, including a
/// statement that somehow evades this check.
/// </summary>
public static class TenancyGuard
{
    /// <summary>
    /// Every table and view carrying <c>org_id</c>. Kept in step with the EF
    /// model by a test comparing the two sets by name, so a new org-scoped
    /// table cannot be added without being protected here.
    /// </summary>
    public static readonly FrozenSet<string> OrgScopedTables = new[]
    {
        "ad_bookings", "advance_settlements", "advances", "advertisers", "api_keys",
        "budget_lines", "cash_ledger", "collection_payments", "collectors",
        "contract_payments", "contracts", "documents", "donors", "expenses",
        "feast_expenses", "feast_tokens", "feasts", "fund_transfers", "lookups",
        "notifications", "offerings", "org_invites", "org_members", "org_role_mappings",
        "puja_calendar_days", "puja_schedule", "puja_years", "push_subscriptions",
        "sessions", "special_donations", "subscriptions", "vendor_issues", "vendors",
        "workflow_items", "workflow_notes", "zones",
        // The reporting views project org_id through and must be scoped too.
        "v_advance_balances", "v_budget_vs_actual", "v_contract_status",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex TableReference = new(
        @"\b(?:FROM|JOIN|INTO|UPDATE)\s+(?:\[?dbo\]?\.)?\[?(?<table>\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches <c>org_id=@org</c>, <c>m.org_id = @org</c> and
    /// <c>[org_id]=@org</c>.
    /// </summary>
    private static readonly Regex OrgPredicate = new(
        @"\[?org_id\]?\s*=\s*@org\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The one table with no org_id of its own: a committee's row IS the
    /// committee, so its id is the tenant. A statement that changes it without
    /// naming the caller's committee would change every committee's row.
    /// </summary>
    private const string RootTable = "organizations";

    private static readonly Regex RootPredicate = new(
        @"\[?\bid\b\]?\s*=\s*@org\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OrgColumn = new(@"\[?org_id\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OrgParameter = new(@"@org\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> ReferencedOrgScopedTables(string sql) =>
        TableReference.Matches(Sanitise(sql))
            .Select(m => m.Groups["table"].Value)
            .Where(OrgScopedTables.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static void AssertScoped(string sql, IEnumerable<string> parameterNames)
    {
        var clean = Sanitise(sql);
        AssertRootTableScoped(clean);

        var scoped = ReferencedOrgScopedTables(sql);
        if (scoped.Count == 0)
        {
            return;
        }

        var hasOrgParameter = parameterNames.Any(n =>
            string.Equals(n.TrimStart('@'), "org", StringComparison.OrdinalIgnoreCase));

        // An INSERT names org_id in its column list and feeds it @org in its
        // values; every other statement must constrain on it in a predicate.
        var isInsert = clean.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase);
        var constrained = isInsert
            ? OrgColumn.IsMatch(clean) && OrgParameter.IsMatch(clean)
            : OrgPredicate.IsMatch(clean);

        if (constrained && hasOrgParameter)
        {
            return;
        }

        throw new TenancyViolationException(
            $"Statement touches org-scoped table(s) [{string.Join(", ", scoped)}] without " +
            "an org_id=@org constraint bound by the executor. " +
            (hasOrgParameter
                ? "The @org parameter is present but the statement does not constrain on it."
                : "No @org parameter was supplied.") +
            " Add the predicate, or query a global table instead.");
    }

    private static void AssertRootTableScoped(string clean)
    {
        var trimmed = clean.TrimStart();
        var mutates = trimmed.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                      || trimmed.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase);
        if (!mutates)
        {
            return;
        }

        var touchesRoot = TableReference.Matches(clean)
            .Any(m => string.Equals(m.Groups["table"].Value, RootTable, StringComparison.OrdinalIgnoreCase));
        if (touchesRoot && !RootPredicate.IsMatch(clean))
        {
            throw new TenancyViolationException(
                "Statement changes the organizations table without `id=@org`, which would affect every committee. " +
                "Constrain it to the caller's own committee.");
        }
    }

    /// <summary>
    /// Blanks out comments and the contents of string literals, so a table
    /// name or an <c>org_id=@org</c> mentioned inside either can neither hide
    /// a real reference nor satisfy the check.
    /// </summary>
    internal static string Sanitise(string sql)
    {
        var result = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            if (c == '\'')
            {
                result.Append("''");
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        // '' is an escaped quote inside the literal.
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }

                        break;
                    }

                    i++;
                }
            }
            else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                result.Append('\n');
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }

                i++;
                result.Append(' ');
            }
            else
            {
                result.Append(c);
            }
        }

        return result.ToString();
    }
}
