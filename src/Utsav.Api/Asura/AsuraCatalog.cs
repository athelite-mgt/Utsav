using System.Collections.Frozen;

namespace Utsav.Api.Asura;

/// <summary>
/// The complete list of database objects Asura — the AI reporting assistant —
/// is allowed to read.
///
/// This is an <b>allowlist, not a denylist</b>, and the distinction is the whole
/// point: a table added to the schema next year is invisible to the assistant
/// until somebody deliberately lists it here. A denylist would expose it by
/// default and rely on whoever added it remembering this file exists.
///
/// <para>
/// Deliberately <b>not</b> <see cref="Endpoints.TableRegistry"/>. That registry is
/// the column whitelist for <i>writes</i>; this one governs <i>reads</i> by a
/// language model. They have opposite purposes and different contents — reusing
/// one for the other would silently grant the assistant sight of anything newly
/// writable, and would deny it the read-only views it most needs. Two names, two
/// lists, on purpose.
/// </para>
///
/// <para>
/// Five tables are excluded outright and must stay that way: <c>users</c>
/// (scrypt password hashes), <c>sessions</c> (live session tokens),
/// <c>api_keys</c> (key hashes), <c>push_subscriptions</c> (browser push
/// secrets) and <c>org_role_mappings</c> (external-identity to role bindings).
/// <c>organizations</c> and <c>notifications</c> are also out: neither answers a
/// reporting question, and the first carries deployment configuration. Boss's
/// instruction was "exclude credentials/secrets, allow everything else" — this
/// is that line drawn concretely. <c>AsuraCatalogTests</c> asserts each of these
/// is absent, so adding one fails a test rather than shipping a leak.
/// </para>
/// </summary>
internal static class AsuraCatalog
{
    /// <summary>
    /// Business tables and the three read-only reporting views. The views exist
    /// in the database already (<c>UtsavDbContext</c> maps them) and do the
    /// budget/advance/contract arithmetic correctly, so letting the assistant
    /// use them beats having it re-derive the same sums and get them subtly
    /// wrong.
    /// </summary>
    public static readonly FrozenSet<string> Objects = new[]
    {
        // Income
        "donors", "subscriptions", "collection_payments",
        "advertisers", "ad_bookings",
        "special_donations", "offerings",

        // Spending
        "expenses", "budget_lines", "vendors",
        "contracts", "contract_payments",
        "advances", "advance_settlements",

        // Treasury
        "cash_ledger", "fund_transfers",

        // Operations
        "puja_schedule", "puja_calendar_days",
        "workflow_items", "workflow_notes",

        // Reference / configuration a report needs to label rows
        "zones", "collectors", "puja_years", "lookups",

        // Metadata only — the catalogue exposes no blob content.
        "documents",

        // Who is on the committee, for "which collector" style questions.
        // Column-level filtering below keeps this to names and roles.
        "org_members",

        // Pre-computed reporting views.
        "v_advance_balances", "v_budget_vs_actual", "v_contract_status",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Never listed in the catalogue. Held explicitly, rather than merely being
    /// absent from <see cref="Objects"/>, so the intent is testable and a future
    /// edit that adds one of these to the list above is caught by a failing
    /// assertion instead of a code review that might not happen.
    /// </summary>
    public static readonly FrozenSet<string> PermanentlyExcluded = new[]
    {
        "users", "sessions", "api_keys", "push_subscriptions",
        "org_role_mappings", "organizations", "notifications",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Column-level redaction, applied on top of the table allowlist as
    /// defence in depth. Two kinds of thing are hidden:
    /// <list type="bullet">
    /// <item>anything whose name looks like a secret, in case a future
    /// migration adds such a column to an already-catalogued table;</item>
    /// <item><c>org_members</c> identity columns — a reporting assistant needs
    /// a member's name and role, never their email or external subject id.</item>
    /// </list>
    /// </summary>
    private static readonly FrozenSet<string> SecretishColumnNames = new[]
    {
        "password", "password_hash", "passwordhash", "salt", "token",
        "refresh_token", "access_token", "secret", "key_hash", "keyhash",
        "api_key", "apikey", "private_key", "p256dh", "auth", "endpoint",
        "external_id", "external_subject", "subject_id",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, FrozenSet<string>> PerTableHiddenColumns =
        new Dictionary<string, FrozenSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["org_members"] = new[] { "email", "phone", "contact", "invited_by", "user_id" }
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool IsReadable(string objectName) => Objects.Contains(objectName);

    /// <summary>
    /// Whether a column of a catalogued table may be shown to the model.
    /// Matches on substring for the secret-ish names so <c>vapid_private_key</c>
    /// is caught by <c>private_key</c> — a new column has to actively avoid
    /// looking like a secret to get through, rather than having to be
    /// predicted exactly.
    /// </summary>
    public static bool IsReadableColumn(string objectName, string columnName)
    {
        if (PerTableHiddenColumns.TryGetValue(objectName, out var hidden)
            && hidden.Contains(columnName))
        {
            return false;
        }

        foreach (var secretish in SecretishColumnNames)
        {
            if (columnName.Contains(secretish, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
