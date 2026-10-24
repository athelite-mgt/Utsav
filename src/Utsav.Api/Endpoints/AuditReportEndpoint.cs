using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Domain.Finance;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// The formal year-end statements a committee hands its CA / society registrar:
/// a Receipts &amp; Payments account (pure cash), an Income &amp; Expenditure
/// account (accrual — what was earned and incurred), and a Balance Sheet as at
/// year end. Computed server-side in one pass so the three tie to each other and
/// to the dashboard's Fund Position, rather than being re-derived in the browser.
///
/// Accounting basis, stated plainly so the numbers are defensible:
///   • Receipts &amp; Payments — cash that physically moved. Opening balances +
///     collections in; expenses + still-outstanding advances + closing balances
///     out. The two sides tie by construction. Advances use the *outstanding*
///     amount (money out with no expense against it yet), so the portion later
///     settled as an expense is never double-counted.
///   • Income &amp; Expenditure — income is billed (earned), not just collected,
///     plus offerings by value; expenditure is expenses incurred. The gap is
///     the year's surplus/deficit and is expected to differ from cash movement.
///   • Balance Sheet — closing cash &amp; bank, dues receivable, advances still
///     out, and offerings received (as assets) against the opening fund plus the
///     year's surplus. It ties: assets = opening fund + surplus. Signed-but-unpaid
///     contract commitments are shown as a memo, not a liability, so they don't
///     unbalance a cash-basis sheet.
/// </summary>
public static class AuditReportEndpoint
{
    public static IEndpointRouteBuilder MapAuditReportEndpoint(this IEndpointRouteBuilder app)
    {
        // Read-only aggregate of finance data — gated on the audit-read
        // permission, the same one behind the History page.
        app.MapGroup("/api/reports")
           .RequirePermission(Permission.ReadAudit)
           .MapGet("/audit", HandleAsync);

        // Same figures, rendered server-side as a PDF. Separate group because
        // it takes the ReportExport policy instead: a signed-in member with
        // audit-read OR a machine presenting an API key (the committee's
        // agent, fetching this on a schedule). See AuthorizationPolicies.
        app.MapGroup("/api/reports")
           .RequireAuthorization(AuthorizationPolicies.ReportExport)
           .MapGet("/audit.pdf", HandlePdfAsync);

        return app;
    }

    /// <summary>
    /// <c>GET /api/reports/audit.pdf</c> — the machine-facing export.
    /// <c>year_id</c> is optional; omitted, it resolves to the committee's
    /// active year, so a scheduled agent needs no per-season config change.
    /// </summary>
    private static async Task<IResult> HandlePdfAsync(Guid? year_id, UtsavDbContext db, CancellationToken ct)
    {
        var yearId = year_id ?? (await ModuleEndpoints.QueryAsync(db,
                "SELECT TOP 1 id FROM puja_years WHERE org_id=@org AND is_active=1 ORDER BY year DESC",
                [new SqlParameter("@org", SessionService.DefaultOrgId)], ct))
            .FirstOrDefault()?["id"] as Guid?;

        if (yearId is null) return Results.NotFound(new { error = "No active puja year" });

        var report = await BuildAsync(yearId.Value, db, ct);
        if (report is null) return Results.NotFound(new { error = "Puja year not found" });

        var bytes = Reports.AuditReportPdf.Render(report);
        return Results.File(bytes, "application/pdf", $"audit-{report.Year}.pdf");
    }

    private static async Task<IResult> HandleAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var report = await BuildAsync(year_id, db, ct);
        return report is null ? Results.NotFound(new { error = "Puja year not found" }) : Results.Ok(report);
    }

    /// <summary>
    /// Builds the whole statement set once, so the JSON endpoint and the
    /// PDF export can never drift apart — the PDF is a rendering of exactly
    /// these figures, not a second computation of them. Null means the year
    /// doesn't exist for this org.
    /// </summary>
    internal static async Task<AuditReportDto?> BuildAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var org = SessionService.DefaultOrgId;
        SqlParameter[] P() => [new SqlParameter("@year", year_id), new SqlParameter("@org", org)];

        // Year header + opening balances brought forward.
        var yearRow = (await ModuleEndpoints.QueryAsync(db, """
            SELECT py.year, py.name, py.opening_cash_balance, py.opening_bank_balance, o.name AS org_name
              FROM puja_years py JOIN organizations o ON o.id = py.org_id
             WHERE py.id=@year AND py.org_id=@org
            """, P(), ct)).FirstOrDefault();

        if (yearRow is null) return null;

        var openingCash = Dec(yearRow["opening_cash_balance"]);
        var openingBank = Dec(yearRow["opening_bank_balance"]);
        var yearNum = yearRow["year"];
        var yearName = yearRow["name"] as string;
        var orgName = yearRow["org_name"] as string ?? "Committee";

        // Collections actually received, per stream, carrying mode so we can
        // split the closing balance across cash and bank.
        var collectionRows = await ModuleEndpoints.QueryAsync(db, """
            SELECT CASE WHEN cp.parent_type='subscription' THEN COALESCE(s.category,'membership')
                        WHEN cp.parent_type='ad_booking' THEN 'ad'
                        ELSE 'other' END AS stream,
                   cp.mode AS mode, cp.amount AS amount
              FROM collection_payments cp
              LEFT JOIN subscriptions s ON s.id = cp.parent_id AND cp.parent_type='subscription'
             WHERE cp.year_id=@year AND cp.org_id=@org
            """, P(), ct);

        // Billed (earned) and dues (still receivable) per stream, for I&E and the
        // balance sheet. One row per stream keeps the three statements aligned.
        var billedRows = await ModuleEndpoints.QueryAsync(db, """
            SELECT category AS stream, SUM(COALESCE(billed,0)) AS billed,
                   SUM(CASE WHEN COALESCE(billed,0)-COALESCE(collected,0) > 0
                            THEN COALESCE(billed,0)-COALESCE(collected,0) ELSE 0 END) AS dues
              FROM subscriptions WHERE year_id=@year AND org_id=@org GROUP BY category
            UNION ALL
            SELECT 'ad', SUM(COALESCE(billed,0)),
                   SUM(CASE WHEN COALESCE(billed,0)-COALESCE(collected,0) > 0
                            THEN COALESCE(billed,0)-COALESCE(collected,0) ELSE 0 END)
              FROM ad_bookings WHERE year_id=@year AND org_id=@org
            UNION ALL
            SELECT 'other', SUM(COALESCE(billed,0)),
                   SUM(CASE WHEN COALESCE(billed,0)-COALESCE(collected,0) > 0
                            THEN COALESCE(billed,0)-COALESCE(collected,0) ELSE 0 END)
              FROM special_donations WHERE year_id=@year AND org_id=@org
            """, P(), ct);

        // Expenses paid, per category, carrying mode for the cash/bank split.
        var expenseRows = await ModuleEndpoints.QueryAsync(db, """
            SELECT COALESCE(NULLIF(category,''),'Uncategorised') AS category, mode AS mode, amount AS amount
              FROM expenses WHERE year_id=@year AND org_id=@org
            """, P(), ct);

        // Advances still out with volunteers, and internal cash<->bank transfers,
        // both needed for the closing cash/bank split.
        var advanceRows = await ModuleEndpoints.QueryAsync(db, """
            SELECT mode AS mode, outstanding AS amount FROM v_advance_balances
             WHERE year_id=@year AND org_id=@org AND status <> 'cancelled'
            """, P(), ct);
        var transferRows = await ModuleEndpoints.QueryAsync(db, """
            SELECT direction AS direction, amount AS amount FROM fund_transfers
             WHERE year_id=@year AND org_id=@org
            """, P(), ct);

        // Offerings by value (in-kind & cash), and signed-but-unpaid contract
        // commitments (a memo, not a balance-sheet liability).
        var offeringsValue = Dec((await ModuleEndpoints.QueryAsync(db,
            "SELECT SUM(COALESCE(value,0)) AS v FROM offerings WHERE year_id=@year AND org_id=@org",
            P(), ct)).FirstOrDefault()?["v"]);
        var contractOutstanding = Dec((await ModuleEndpoints.QueryAsync(db,
            "SELECT SUM(CASE WHEN outstanding > 0 THEN outstanding ELSE 0 END) AS v FROM v_contract_status WHERE year_id=@year AND org_id=@org AND status <> 'cancelled'",
            P(), ct)).FirstOrDefault()?["v"]);

        // --- Fold rows into the figures the statements need ---
        string[] streamOrder = ["membership", "special", "ad", "other"];
        var streamLabel = new Dictionary<string, string>
        {
            ["membership"] = "Membership subscriptions",
            ["special"] = "Special donations",
            ["ad"] = "Advertisements",
            ["other"] = "Other income",
        };

        var collectedByStream = streamOrder.ToDictionary(s => s, _ => 0m);
        decimal cashIn = 0m, bankIn = 0m;
        foreach (var r in collectionRows)
        {
            var stream = (r["stream"] as string) ?? "other";
            if (!collectedByStream.ContainsKey(stream)) stream = "other";
            var amt = Dec(r["amount"]);
            collectedByStream[stream] += amt;
            if (PaymentModeClassifier.BucketOf(r["mode"] as string) == FundSideKind.Bank) bankIn += amt; else cashIn += amt;
        }

        var billedByStream = streamOrder.ToDictionary(s => s, _ => 0m);
        var duesByStream = streamOrder.ToDictionary(s => s, _ => 0m);
        foreach (var r in billedRows)
        {
            var stream = (r["stream"] as string) ?? "other";
            if (!billedByStream.ContainsKey(stream)) stream = "other";
            billedByStream[stream] += Dec(r["billed"]);
            duesByStream[stream] += Dec(r["dues"]);
        }

        var expenseByCategory = new SortedDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        decimal cashOut = 0m, bankOut = 0m;
        foreach (var r in expenseRows)
        {
            var cat = (r["category"] as string) ?? "Uncategorised";
            var amt = Dec(r["amount"]);
            expenseByCategory[cat] = expenseByCategory.GetValueOrDefault(cat) + amt;
            if (PaymentModeClassifier.BucketOf(r["mode"] as string) == FundSideKind.Bank) bankOut += amt; else cashOut += amt;
        }

        decimal cashAdvOut = 0m, bankAdvOut = 0m;
        foreach (var r in advanceRows)
        {
            var amt = Dec(r["amount"]);
            if (PaymentModeClassifier.BucketOf(r["mode"] as string) == FundSideKind.Bank) bankAdvOut += amt; else cashAdvOut += amt;
        }

        decimal cashXferIn = 0m, cashXferOut = 0m, bankXferIn = 0m, bankXferOut = 0m;
        foreach (var r in transferRows)
        {
            var amt = Dec(r["amount"]);
            if ((r["direction"] as string) == "bank_to_cash") { cashXferIn += amt; bankXferOut += amt; }
            else { bankXferIn += amt; cashXferOut += amt; }
        }

        var totalCollected = collectedByStream.Values.Sum();
        var totalExpenses = expenseByCategory.Values.Sum();
        var totalAdvOut = cashAdvOut + bankAdvOut;
        var totalDues = duesByStream.Values.Sum();
        var totalBilled = billedByStream.Values.Sum();

        // Closing balances, split by pocket. Same formula the Fund Position uses,
        // so the audit report and the dashboard can never disagree.
        var closingCash = openingCash + cashIn - cashOut + cashXferIn - cashXferOut - cashAdvOut;
        var closingBank = openingBank + bankIn - bankOut + bankXferIn - bankXferOut - bankAdvOut;
        var closingTotal = closingCash + closingBank;

        // --- Receipts & Payments (cash) ---
        var receipts = new List<LineItem>
        {
            new("To Opening balance — Cash in hand", openingCash),
            new("To Opening balance — Bank / online", openingBank),
        };
        foreach (var s in streamOrder)
            if (collectedByStream[s] != 0) receipts.Add(new($"To {streamLabel[s]} collected", collectedByStream[s]));
        var totalReceiptsSide = openingCash + openingBank + totalCollected;

        var payments = new List<LineItem>();
        foreach (var (cat, amt) in expenseByCategory)
            payments.Add(new($"By {cat}", amt));
        if (totalAdvOut != 0) payments.Add(new("By Advances to volunteers (unsettled)", totalAdvOut));
        payments.Add(new("By Closing balance — Cash in hand", closingCash));
        payments.Add(new("By Closing balance — Bank / online", closingBank));
        var totalPaymentsSide = totalExpenses + totalAdvOut + closingTotal;

        // --- Income & Expenditure (accrual) ---
        var income = new List<LineItem>();
        foreach (var s in streamOrder)
            if (billedByStream[s] != 0) income.Add(new(streamLabel[s], billedByStream[s]));
        if (offeringsValue != 0) income.Add(new("Offerings received (by value)", offeringsValue));
        var totalIncome = totalBilled + offeringsValue;

        var expenditure = new List<LineItem>();
        foreach (var (cat, amt) in expenseByCategory)
            expenditure.Add(new(cat, amt));
        var totalExpenditure = totalExpenses;
        var surplus = totalIncome - totalExpenditure;

        // --- Balance Sheet (as at year end) ---
        var assets = new List<LineItem>
        {
            new("Cash in hand", closingCash),
            new("Bank / online balance", closingBank),
        };
        if (totalDues != 0) assets.Add(new("Subscriptions & bills receivable (dues)", totalDues));
        if (totalAdvOut != 0) assets.Add(new("Advances with volunteers", totalAdvOut));
        if (offeringsValue != 0) assets.Add(new("Offerings received (in-kind & cash)", offeringsValue));
        var totalAssets = closingTotal + totalDues + totalAdvOut + offeringsValue;

        var funds = new List<LineItem>
        {
            new("Opening fund brought forward", openingCash + openingBank),
            new(surplus >= 0 ? "Add: Surplus for the year" : "Less: Deficit for the year", surplus),
        };
        var totalFunds = openingCash + openingBank + surplus;

        var notes = new List<string>();
        if (contractOutstanding != 0)
            notes.Add($"Commitments: signed vendor contracts with ₹{contractOutstanding:N0} still unpaid at year end (not a liability above — shown for the CA's information).");
        notes.Add("Receipts & Payments is on a cash basis; Income & Expenditure is on an accrual (billed) basis. The difference between them is timing — dues billed but not yet collected, and advances still out.");

        // --- Annexures: the donor/vendor-level detail behind the summary
        // figures above, for the CA to trace any total back to source rows.
        var membershipAnnexure = await IncomeAnnexureAsync(db, "membership", year_id, org, ct);
        var specialAnnexure = await IncomeAnnexureAsync(db, "special", year_id, org, ct);
        var adAnnexure = await AdAnnexureAsync(db, year_id, org, ct);
        var otherAnnexure = await OtherIncomeAnnexureAsync(db, year_id, org, ct);
        var expenseAnnexure = await ExpenseAnnexureAsync(db, year_id, org, ct);

        return new AuditReportDto(
            orgName,
            yearName is { Length: > 0 } ? yearName : $"Durga Puja {yearNum}",
            Convert.ToInt32(yearNum),
            new BalanceDto(openingCash, openingBank, openingCash + openingBank),
            new BalanceDto(closingCash, closingBank, closingTotal),
            new StatementDto(receipts, payments, totalReceiptsSide, totalPaymentsSide),
            new IncExpDto(income, expenditure, totalIncome, totalExpenditure, surplus),
            new BalanceSheetDto(assets, funds, totalAssets, totalFunds),
            notes,
            new AnnexuresDto(membershipAnnexure, specialAnnexure, adAnnexure, otherAnnexure, expenseAnnexure));
    }

    // Membership and Special Donation are both rows in `subscriptions`,
    // distinguished only by `category` — same donor/zone/bill-no shape.
    private static async Task<IReadOnlyList<IncomeAnnexureRow>> IncomeAnnexureAsync(
        UtsavDbContext db, string category, Guid yearId, Guid org, CancellationToken ct)
    {
        var rows = await ModuleEndpoints.QueryAsync(db, """
            SELECT d.name AS donor_name, d.surname AS donor_surname, z.name AS zone_name,
                   s.bill_no, s.billed, s.billed_on, s.collected, s.collected_on, s.due, s.mode
              FROM subscriptions s
              JOIN donors d ON d.id = s.donor_id
              LEFT JOIN zones z ON z.id = d.zone_id
             WHERE s.year_id = @year AND s.org_id = @org AND s.category = @category
             ORDER BY d.name, d.surname
            """, [new SqlParameter("@year", yearId), new SqlParameter("@org", org), new SqlParameter("@category", category)], ct);

        return FilterBilled(rows.Select(r => new IncomeAnnexureRow(
            Name(r["donor_name"] as string, r["donor_surname"] as string),
            r["zone_name"] as string,
            r["bill_no"] as string,
            Dec(r["billed"]), r["billed_on"] as string,
            Dec(r["collected"]), r["collected_on"] as string,
            Dec(r["due"]), r["mode"] as string)));
    }

    /// <summary>
    /// A donor with no bill raised this year (billed 0 or never set) has
    /// nothing for a CA to trace on this annexure — only donors actually
    /// billed belong here. Extracted as a pure function so it's unit-testable
    /// without a database (see AuditReportEndpointTests).
    /// </summary>
    internal static List<IncomeAnnexureRow> FilterBilled(IEnumerable<IncomeAnnexureRow> rows) =>
        rows.Where(r => r.Billed != 0).ToList();

    private static async Task<IReadOnlyList<IncomeAnnexureRow>> AdAnnexureAsync(
        UtsavDbContext db, Guid yearId, Guid org, CancellationToken ct)
    {
        var rows = await ModuleEndpoints.QueryAsync(db, """
            SELECT a.name AS advertiser_name,
                   ab.bill_no, ab.billed, ab.billed_on, ab.collected, ab.collected_on, ab.due, ab.mode
              FROM ad_bookings ab
              JOIN advertisers a ON a.id = ab.advertiser_id
             WHERE ab.year_id = @year AND ab.org_id = @org
             ORDER BY a.name
            """, [new SqlParameter("@year", yearId), new SqlParameter("@org", org)], ct);

        return rows.Select(r => new IncomeAnnexureRow(
            r["advertiser_name"] as string ?? "—", null,
            r["bill_no"] as string,
            Dec(r["billed"]), r["billed_on"] as string,
            Dec(r["collected"]), r["collected_on"] as string,
            Dec(r["due"]), r["mode"] as string)).ToList();
    }

    // The `special_donations` table backs the "Other Incomes" page in the UI
    // (a historical name from before that page was split off) — it has no
    // donor/zone link and no separate billed_on, just a name of record.
    private static async Task<IReadOnlyList<IncomeAnnexureRow>> OtherIncomeAnnexureAsync(
        UtsavDbContext db, Guid yearId, Guid org, CancellationToken ct)
    {
        var rows = await ModuleEndpoints.QueryAsync(db, """
            SELECT name, bill_no, billed, collected, collected_on, due, mode
              FROM special_donations
             WHERE year_id = @year AND org_id = @org
             ORDER BY name
            """, [new SqlParameter("@year", yearId), new SqlParameter("@org", org)], ct);

        return rows.Select(r => new IncomeAnnexureRow(
            r["name"] as string ?? "—", null,
            r["bill_no"] as string,
            Dec(r["billed"]), null,
            Dec(r["collected"]), r["collected_on"] as string,
            Dec(r["due"]), r["mode"] as string)).ToList();
    }

    private static async Task<IReadOnlyList<ExpenseAnnexureRow>> ExpenseAnnexureAsync(
        UtsavDbContext db, Guid yearId, Guid org, CancellationToken ct)
    {
        var rows = await ModuleEndpoints.QueryAsync(db, """
            SELECT COALESCE(NULLIF(e.category,''),'Uncategorised') AS category, e.details, e.bill_no,
                   e.amount, e.spent_on, e.mode
              FROM expenses e
             WHERE e.year_id = @year AND e.org_id = @org
             ORDER BY e.spent_on, e.category
            """, [new SqlParameter("@year", yearId), new SqlParameter("@org", org)], ct);

        return rows.Select(r => new ExpenseAnnexureRow(
            r["category"] as string ?? "Uncategorised",
            r["details"] as string,
            r["bill_no"] as string,
            Dec(r["amount"]),
            r["spent_on"] as string,
            r["mode"] as string)).ToList();
    }

    private static string Name(string? name, string? surname) =>
        string.Join(" ", new[] { name, surname }.Where(p => !string.IsNullOrWhiteSpace(p))) is { Length: > 0 } n ? n : "—";

    private static decimal Dec(object? v) => v is decimal d ? d : (v is null or DBNull ? 0m : Convert.ToDecimal(v));

    internal sealed record LineItem(
        [property: JsonPropertyName("label")] string Label,
        [property: JsonPropertyName("amount")] decimal Amount);

    internal sealed record BalanceDto(
        [property: JsonPropertyName("cash")] decimal Cash,
        [property: JsonPropertyName("bank")] decimal Bank,
        [property: JsonPropertyName("total")] decimal Total);

    internal sealed record StatementDto(
        [property: JsonPropertyName("receipts")] IReadOnlyList<LineItem> Receipts,
        [property: JsonPropertyName("payments")] IReadOnlyList<LineItem> Payments,
        [property: JsonPropertyName("total_receipts")] decimal TotalReceipts,
        [property: JsonPropertyName("total_payments")] decimal TotalPayments);

    internal sealed record IncExpDto(
        [property: JsonPropertyName("income")] IReadOnlyList<LineItem> Income,
        [property: JsonPropertyName("expenditure")] IReadOnlyList<LineItem> Expenditure,
        [property: JsonPropertyName("total_income")] decimal TotalIncome,
        [property: JsonPropertyName("total_expenditure")] decimal TotalExpenditure,
        [property: JsonPropertyName("surplus")] decimal Surplus);

    internal sealed record BalanceSheetDto(
        [property: JsonPropertyName("assets")] IReadOnlyList<LineItem> Assets,
        [property: JsonPropertyName("funds")] IReadOnlyList<LineItem> Funds,
        [property: JsonPropertyName("total_assets")] decimal TotalAssets,
        [property: JsonPropertyName("total_funds")] decimal TotalFunds);

    /// <summary>One donor/advertiser's income row for a Membership, Special Donation, Advertisements or Other Incomes annexure.</summary>
    internal sealed record IncomeAnnexureRow(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("zone")] string? Zone,
        [property: JsonPropertyName("bill_no")] string? BillNo,
        [property: JsonPropertyName("billed")] decimal Billed,
        [property: JsonPropertyName("billed_on")] string? BilledOn,
        [property: JsonPropertyName("collected")] decimal Collected,
        [property: JsonPropertyName("collected_on")] string? CollectedOn,
        [property: JsonPropertyName("due")] decimal Due,
        [property: JsonPropertyName("mode")] string? Mode);

    /// <summary>
    /// One expense row for the Expenses annexure. <c>bill_no</c> is labelled
    /// "Voucher" on the frontend — same underlying data, display name only,
    /// so the wire field stays <c>bill_no</c>.
    /// </summary>
    internal sealed record ExpenseAnnexureRow(
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("details")] string? Details,
        [property: JsonPropertyName("bill_no")] string? BillNo,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("spent_on")] string? SpentOn,
        [property: JsonPropertyName("mode")] string? Mode);

    internal sealed record AnnexuresDto(
        [property: JsonPropertyName("membership")] IReadOnlyList<IncomeAnnexureRow> Membership,
        [property: JsonPropertyName("special")] IReadOnlyList<IncomeAnnexureRow> Special,
        [property: JsonPropertyName("ads")] IReadOnlyList<IncomeAnnexureRow> Ads,
        [property: JsonPropertyName("other")] IReadOnlyList<IncomeAnnexureRow> Other,
        [property: JsonPropertyName("expenses")] IReadOnlyList<ExpenseAnnexureRow> Expenses);

    internal sealed record AuditReportDto(
        [property: JsonPropertyName("org_name")] string OrgName,
        [property: JsonPropertyName("year_label")] string YearLabel,
        [property: JsonPropertyName("year")] int Year,
        [property: JsonPropertyName("opening")] BalanceDto Opening,
        [property: JsonPropertyName("closing")] BalanceDto Closing,
        [property: JsonPropertyName("receipts_payments")] StatementDto ReceiptsPayments,
        [property: JsonPropertyName("income_expenditure")] IncExpDto IncomeExpenditure,
        [property: JsonPropertyName("balance_sheet")] BalanceSheetDto BalanceSheet,
        [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes,
        [property: JsonPropertyName("annexures")] AnnexuresDto Annexures);
}
