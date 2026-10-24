using System.Collections.Frozen;

namespace Utsav.Infrastructure.Audit;

/// <summary>
/// How each table shows up in the History page — the friendly "page" name a
/// committee member would recognise, and how to name the specific record that
/// changed (so an edit reads "Ramesh Gupta · Collected 500 → 1000", not
/// "subscriptions row a1b2…").
///
/// Most tables carry a human name inline (<see cref="NameColumns"/>, tried in
/// order). A few only carry a foreign key — a subscription's donor name lives
/// on <c>donors</c>, an ad booking's advertiser on <c>advertisers</c> — so
/// those also set <see cref="FkTable"/>/<see cref="FkIdColumn"/>/<see cref="FkNameColumn"/>
/// and the label is resolved with one small lookup at write time.
/// </summary>
public static class AuditableTables
{
    public sealed record Meta(
        string Page,
        string[] NameColumns,
        string? FkTable = null,
        string? FkIdColumn = null,
        string? FkNameColumn = null);

    public static readonly FrozenDictionary<string, Meta> Map =
        new Dictionary<string, Meta>(StringComparer.Ordinal)
        {
            // Empty NameColumns: the donor/advertiser's own name (via the FK
            // lookup below) is what a reader wants — "Subir Chatterjee", not
            // bill number "1". Bill numbers aren't unique across years either,
            // so they'd be actively misleading as a record label.
            ["subscriptions"] = new("Membership / Special Donation", [], "donors", "donor_id", "name"),
            ["feasts"] = new("Feast", [], "donors", "donor_id", "name"),
            // The code itself ("F2026-0001") is the readable identifier here —
            // unlike feasts, a token's own donor is one hop further away
            // (feast_tokens.feast_id -> feasts.donor_id -> donors.name) than
            // the single-table FK lookup below can reach.
            ["feast_tokens"] = new("Feast Entry Tokens", ["code"]),
            // Label is resolved and passed in directly by CrudEndpoints.PersistCollectionPaymentsAsync
            // (the parent is polymorphic — donor, advertiser or special donation — which the single
            // FkTable/FkIdColumn shape above can't express), so no NameColumns/FkTable needed here.
            ["collection_payments"] = new("Collections", []),
            ["ad_bookings"] = new("Advertising", [], "advertisers", "advertiser_id", "name"),
            ["special_donations"] = new("Other Incomes", ["name"]),
            ["offerings"] = new("Offerings", ["name"]),
            ["expenses"] = new("Expenses", ["details", "category"]),
            ["feast_expenses"] = new("Feast Expenses", ["details", "category"]),
            ["budget_lines"] = new("Budget", ["category"]),
            ["advances"] = new("Cash Advances", ["issued_to_name"]),
            ["contracts"] = new("Vendor Contracts", ["title"]),
            ["contract_payments"] = new("Vendor Contracts", ["milestone"]),
            ["vendors"] = new("Vendors", ["name"]),
            ["vendor_issues"] = new("Vendor Issues", [], "vendors", "vendor_id", "name"),
            ["donors"] = new("Donors", ["name"]),
            ["advertisers"] = new("Advertisers", ["name"]),
            ["puja_years"] = new("Puja Year", ["name"]),
            ["puja_schedule"] = new("Puja Schedule", ["charge_title"]),
            ["workflow_items"] = new("Task Tracker", ["title"]),
            ["workflow_notes"] = new("Task Tracker", ["note"]),
            ["fund_transfers"] = new("Fund Transfer", ["reference", "direction"]),
            ["cash_ledger"] = new("Cash Ledger", ["party"]),
            ["documents"] = new("Documents", ["title"]),
            ["zones"] = new("Zones", ["name"]),
            ["collectors"] = new("Collectors", ["name"]),
            ["lookups"] = new("Dropdown Lists", ["label"]),
            ["org_members"] = new("Members & Roles", [], "users", "user_id", "full_name"),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The friendly page name, or the raw table name if it isn't one we've labelled.</summary>
    public static string PageFor(string table) => Map.TryGetValue(table, out var meta) ? meta.Page : table;
}
