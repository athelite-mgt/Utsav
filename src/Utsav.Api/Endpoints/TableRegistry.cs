using System.Collections.Frozen;
using Utsav.Domain.Authorization;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Column whitelist for the generic CRUD routes, ported verbatim from the
/// <c>TABLES</c> registry in <c>server/app.mjs</c>.
///
/// This is a security boundary, not a convenience. A table or column name
/// cannot be parameterised in SQL, so the only safe way to build these
/// statements is to never let request text reach the query at all: the client's
/// table name is used solely as a dictionary lookup, and each body key must
/// match an entry in that table's column list before it is used. Anything not
/// listed here is silently dropped rather than rejected, exactly as the Node
/// version behaves.
///
/// Two columns are deliberately absent everywhere: <c>due</c>, which the
/// database computes, and <c>org_id</c>, which is always stamped from the
/// caller's session and never accepted from the request body.
/// </summary>
internal static class TableRegistry
{
    public static readonly FrozenDictionary<string, FrozenSet<string>> Tables =
        new Dictionary<string, string[]>
        {
            ["donors"] = ["name", "surname", "contact", "zone_id", "notes"],
            ["subscriptions"] = ["donor_id", "year_id", "category", "prior_billed", "projected", "bill_no", "billed", "billed_on", "collected", "mode", "collected_on", "collector_id", "comments", "history", "updated_at", "not_eligible"],
            ["feasts"] = ["donor_id", "year_id", "bill_no", "billed", "billed_on", "collected", "mode", "collected_on", "collector_id", "comments", "updated_at", "not_eligible"],
            ["advertisers"] = ["name", "contact", "notes"],
            ["ad_bookings"] = ["advertiser_id", "year_id", "ad_type", "projected", "bill_no", "billed", "billed_on", "collected", "mode", "status", "collected_on", "comments", "updated_at"],
            ["special_donations"] = ["year_id", "name", "contact", "projected", "bill_no", "billed", "collected", "mode", "collected_on", "comments", "updated_at"],
            ["offerings"] = ["year_id", "name", "contact", "offering_type", "puja_day", "value", "details", "expected_date", "received_date", "received_by", "status"],
            ["expenses"] = ["year_id", "category", "details", "amount", "spent_on", "mode", "comment", "vendor_id", "contract_id", "bill_no"],
            ["feast_expenses"] = ["year_id", "category", "details", "amount", "spent_on", "mode", "comment", "vendor_id", "bill_no"],
            ["budget_lines"] = ["year_id", "category", "notes", "forecast", "contract_amount", "sort_order"],
            ["cash_ledger"] = ["year_id", "party", "amount", "entry_date", "note"],
            ["fund_transfers"] = ["year_id", "direction", "amount", "transfer_date", "reference", "note"],
            ["vendors"] = ["name", "contact", "email", "address", "gst_no", "category", "active", "notes"],
            ["puja_schedule"] = ["year_id", "puja_day", "event_date", "start_time", "end_time", "charge_title", "assignee_name", "assignee_collector_id", "contact", "notes", "sort_order"],
            ["puja_calendar_days"] = ["year_id", "label", "event_date", "note", "sort_order"],
            ["workflow_items"] = ["year_id", "title", "category", "owner_name", "owner_collector_id", "status", "due_date", "completed_date", "notes", "sort_order", "updated_at"],
            ["lookups"] = ["category", "label", "sort_order", "active"],
            ["zones"] = ["name", "sort_order"],
            ["collectors"] = ["name", "phone", "user_id", "active", "sort_order"],
            ["puja_years"] = ["year", "name", "target", "projection_pct", "opening_cash_balance", "opening_bank_balance", "is_active", "notes"],
        }.ToFrozenDictionary(pair => pair.Key, pair => pair.Value.ToFrozenSet(StringComparer.Ordinal));

    /// <summary>
    /// Rows that point at a table whose foreign key had to be demoted from
    /// <c>ON DELETE SET NULL</c> to NO ACTION so SQL Server would accept the
    /// schema — see <c>UtsavDbContext.BreakSecondaryCascadePaths</c>. Postgres
    /// nulled these automatically; here the delete handler does it explicitly
    /// first, which keeps the observable behaviour identical rather than
    /// turning a working delete into a foreign-key error.
    /// </summary>
    public static readonly FrozenDictionary<string, (string Table, string Column)[]> DetachBeforeDelete =
        new Dictionary<string, (string, string)[]>
        {
            ["expenses"] = [("advance_settlements", "expense_id")],
            ["feasts"] = [("feast_tokens", "feast_id")],
        }.ToFrozenDictionary();

    /// <summary>
    /// Which <see cref="Permission"/> a write to each table requires — the
    /// generic CRUD route is one URL shape (<c>/api/{table}</c>) covering
    /// tables from every area of <see cref="PermissionMatrix"/>, so the
    /// permission can't be fixed on the route the way it is for the
    /// module-specific endpoints; it has to be looked up per table, at the
    /// point the table name is resolved.
    /// </summary>
    public static readonly FrozenDictionary<string, Permission> RequiredPermission =
        new Dictionary<string, Permission>
        {
            // Membership/special donation, ads, other incomes, offerings.
            ["donors"] = Permission.WriteIncome,
            ["subscriptions"] = Permission.WriteIncome,
            ["feasts"] = Permission.WriteIncome,
            // Feast spending is gated the same as feast income — the feast
            // is run as its own self-contained unit, not folded into the
            // general Finance role's expenses/budget/vendors.
            ["feast_expenses"] = Permission.WriteIncome,
            ["advertisers"] = Permission.WriteIncome,
            ["ad_bookings"] = Permission.WriteIncome,
            ["special_donations"] = Permission.WriteIncome,
            ["offerings"] = Permission.WriteIncome,

            // Expenses, budget, vendors, cash ledger, transfers.
            ["expenses"] = Permission.WriteFinance,
            ["budget_lines"] = Permission.WriteFinance,
            ["cash_ledger"] = Permission.WriteFinance,
            ["fund_transfers"] = Permission.WriteFinance,
            ["vendors"] = Permission.WriteFinance,

            // Workflow / tasks, puja-day schedule, festival calendar.
            ["puja_schedule"] = Permission.WriteWorkflow,
            ["puja_calendar_days"] = Permission.WriteWorkflow,
            ["workflow_items"] = Permission.WriteWorkflow,

            // Committee configuration — admin-exclusive.
            ["lookups"] = Permission.ManageSettings,
            ["zones"] = Permission.ManageSettings,
            ["collectors"] = Permission.ManageSettings,
            ["puja_years"] = Permission.ManageSettings,
        }.ToFrozenDictionary();

    public static bool IsKnown(string table) => Tables.ContainsKey(table);

    /// <summary>
    /// Resolves a caller-supplied table name to the identical string stored in
    /// <see cref="Tables"/>, and yields that instance rather than the caller's.
    ///
    /// The two are equal by definition — the lookup only succeeds on an exact
    /// ordinal match — so this changes nothing at runtime. It matters for
    /// reading the code: every identifier interpolated into a SQL statement can
    /// then be traced back to a literal in this file, instead of to a string
    /// that arrived over HTTP and was merely checked along the way.
    /// </summary>
    public static bool TryResolve(string requested, out string table, out FrozenSet<string> columns)
    {
        foreach (var known in Tables.Keys)
        {
            if (string.Equals(known, requested, StringComparison.Ordinal))
            {
                table = known;
                columns = Tables[known];
                return true;
            }
        }

        table = string.Empty;
        columns = FrozenSet<string>.Empty;
        return false;
    }

    /// <summary>Same idea as <see cref="TryResolve"/>, for column names.</summary>
    public static bool TryResolveColumn(FrozenSet<string> columns, string requested, out string column)
    {
        foreach (var known in columns)
        {
            if (string.Equals(known, requested, StringComparison.Ordinal))
            {
                column = known;
                return true;
            }
        }

        column = string.Empty;
        return false;
    }
}
