using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Finance;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Where the committee's money physically is, right now — ported from
/// <c>GET /api/fund_position</c> in <c>server/app.mjs</c>.
///
/// <code>balance = collected − spent + transfers in − transfers out − advances still out</code>
///
/// The advance term is what keeps this honest. Cash handed to a volunteer has
/// left the box even though no expense exists yet; once they settle it, the
/// outstanding drops to zero and the expense they filed takes its place, so the
/// rupee is counted exactly once at every point in the cycle.
///
/// Still a single round trip. The original did that because six parallel
/// queries exhausted Supabase's pooler; on Azure SQL the pressure is gone, but
/// one query over six small result sets is simply less work than six.
/// </summary>
public static class FundPositionEndpoint
{
    // Collections come from the per-instalment ledger (collection_payments),
    // not the parents' single `collected`/`mode`, so a donor who paid part in
    // cash and part online lands in the right pockets. Existing collections were
    // backfilled into that ledger (see AddCollectionPayments), so historical
    // totals are unaffected.
    //
    // Feast is deliberately excluded: it's its own pool of money, kept apart
    // from the committee's general fund (and from whatever expense tracking
    // Feast eventually gets), so it must never bump "cash in hand" / "bank
    // balance" here even though its instalments live in this same table.
    private const string Sql = """
        SELECT 'in' AS kind, amount, mode AS tag,
               CAST(NULL AS NVARCHAR(MAX)) AS note, CAST(NULL AS NVARCHAR(MAX)) AS on_date
          FROM collection_payments WHERE year_id=@year AND org_id=@org AND parent_type <> 'feast'
        UNION ALL
        SELECT 'out', amount,      mode,      NULL, NULL FROM expenses          WHERE year_id=@year AND org_id=@org
        UNION ALL
        SELECT 'adv', outstanding, mode,      NULL, NULL FROM v_advance_balances
         WHERE year_id=@year AND org_id=@org AND status <> 'cancelled'
        UNION ALL
        SELECT 'xfer', amount, direction, note, transfer_date FROM fund_transfers WHERE year_id=@year AND org_id=@org
        """;

    public static IEndpointRouteBuilder MapFundPositionEndpoint(this IEndpointRouteBuilder app)
    {
        // Deliberately computed server-side rather than in the browser: it needs
        // advance balances, which are admin-only data, but the aggregate is safe
        // for any member to see.
        app.MapGet("/api/fund_position", HandleAsync)
           .RequireAuthorization(AuthorizationPolicies.ActiveMember);
        app.MapGet("/api/fund_position/breakdown", BreakdownAsync)
           .RequireAuthorization(AuthorizationPolicies.ActiveMember);

        return app;
    }

    /// <summary>
    /// Who the cash-in-hand and bank balances on the dashboard actually came
    /// from — one row per payer, summed across every instalment they paid on
    /// that side, for the "tap the card to see who" breakdown. Same
    /// <see cref="PaymentModeClassifier"/> bucketing as the headline figures,
    /// so the two always agree.
    /// </summary>
    private static async Task<IResult> BreakdownAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var sql = """
            SELECT cp.amount, cp.mode,
                   CASE WHEN cp.parent_type='subscription' THEN LTRIM(RTRIM(COALESCE(d.name,'') + ' ' + COALESCE(d.surname,'')))
                        WHEN cp.parent_type='ad_booking' THEN a.name
                        WHEN cp.parent_type='special_donation' THEN sd.name
                        ELSE NULL END AS payer_name
              FROM collection_payments cp
              LEFT JOIN subscriptions s ON s.id = cp.parent_id AND cp.parent_type = 'subscription'
              LEFT JOIN donors d ON d.id = s.donor_id
              LEFT JOIN ad_bookings ab ON ab.id = cp.parent_id AND cp.parent_type = 'ad_booking'
              LEFT JOIN advertisers a ON a.id = ab.advertiser_id
              LEFT JOIN special_donations sd ON sd.id = cp.parent_id AND cp.parent_type = 'special_donation'
             WHERE cp.year_id = @year AND cp.org_id = @org AND cp.parent_type <> 'feast'
            """;

        var rows = await ModuleEndpoints.QueryAsync(db, sql,
            [new SqlParameter("@year", year_id), new SqlParameter("@org", db.TenantId)], ct);

        var cash = new Dictionary<string, decimal>();
        var bank = new Dictionary<string, decimal>();
        foreach (var row in rows)
        {
            var amount = row["amount"] is decimal d ? d : 0m;
            var name = row["payer_name"] as string;
            if (string.IsNullOrWhiteSpace(name)) name = "Unknown";

            var bucket = PaymentModeClassifier.BucketOf(row["mode"] as string) == FundSideKind.Bank ? bank : cash;
            bucket[name] = bucket.GetValueOrDefault(name) + amount;
        }

        static IReadOnlyList<EntryDto> ToEntries(Dictionary<string, decimal> d) =>
            d.Select(kv => new EntryDto(kv.Key, kv.Value)).OrderByDescending(e => e.Amount).ToList();

        return Results.Ok(new BreakdownDto(ToEntries(cash), ToEntries(bank)));
    }

    private static async Task<IResult> HandleAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await ModuleEndpoints.QueryAsync(db, Sql,
            [new SqlParameter("@year", year_id), new SqlParameter("@org", db.TenantId)], ct);

        // Balances carried forward from last year's close — real money on hand
        // before a rupee of this year's collection, so they belong in the
        // position but not in "received". Kept as their own term (see Side.Opening)
        // so the card can show them for what they are.
        var year = await db.PujaYears.AsNoTracking()
            .Where(y => y.Id == year_id && y.OrgId == db.TenantId)
            .Select(y => new { y.OpeningCashBalance, y.OpeningBankBalance })
            .SingleOrDefaultAsync(ct);

        var cash = new Side { Opening = year?.OpeningCashBalance ?? 0m };
        var bank = new Side { Opening = year?.OpeningBankBalance ?? 0m };
        var transfers = new List<TransferRow>();

        foreach (var row in rows)
        {
            var amount = row["amount"] is decimal d ? d : 0m;
            var tag = row["tag"] as string;

            if ((string?)row["kind"] == "xfer")
            {
                // `tag` carries the transfer direction for these rows, not a
                // payment mode.
                if (tag == "bank_to_cash")
                {
                    cash.TransfersIn += amount;
                    bank.TransfersOut += amount;
                }
                else
                {
                    bank.TransfersIn += amount;
                    cash.TransfersOut += amount;
                }

                transfers.Add(new TransferRow(tag, amount, row["note"] as string, row["on_date"] as string));
                continue;
            }

            var side = PaymentModeClassifier.BucketOf(tag) == FundSideKind.Bank ? bank : cash;
            switch ((string?)row["kind"])
            {
                case "in": side.Received += amount; break;
                case "out": side.Spent += amount; break;
                default: side.AdvancesOut += amount; break;
            }
        }

        // String comparison on the date, matching the original's localeCompare
        // — these columns are free-text dates, not a date type.
        var recentTransfers = transfers
            .OrderByDescending(t => t.TransferDate ?? string.Empty, StringComparer.Ordinal)
            .Take(3)
            .ToList();

        return Results.Ok(new FundPositionDto(
            cash.ToDto(),
            bank.ToDto(),
            recentTransfers,
            new TotalDto(
                cash.Opening + bank.Opening,
                cash.Received + bank.Received,
                cash.Spent + bank.Spent,
                cash.AdvancesOut + bank.AdvancesOut,
                cash.Balance + bank.Balance)));
    }

    private sealed class Side
    {
        public decimal Opening;
        public decimal Received;
        public decimal Spent;
        public decimal TransfersIn;
        public decimal TransfersOut;
        public decimal AdvancesOut;

        public decimal Balance => Opening + Received - Spent + TransfersIn - TransfersOut - AdvancesOut;

        public FundSideDto ToDto() =>
            new(Opening, Received, Spent, TransfersIn, TransfersOut, AdvancesOut, Balance);
    }

    // This one response is camelCase, unlike the rest of the API, because it is
    // assembled here rather than echoed from database rows — see FundPosition
    // in src/types/db.ts. `transfer_date` alone stays snake_case, since it
    // passes straight through from the fund_transfers column of that name.
    // Every property is therefore named explicitly rather than left to the
    // global snake_case policy, which would rename all of them.
    private sealed record FundSideDto(
        [property: JsonPropertyName("opening")] decimal Opening,
        [property: JsonPropertyName("received")] decimal Received,
        [property: JsonPropertyName("spent")] decimal Spent,
        [property: JsonPropertyName("transfersIn")] decimal TransfersIn,
        [property: JsonPropertyName("transfersOut")] decimal TransfersOut,
        [property: JsonPropertyName("advancesOut")] decimal AdvancesOut,
        [property: JsonPropertyName("balance")] decimal Balance);

    private sealed record TotalDto(
        [property: JsonPropertyName("opening")] decimal Opening,
        [property: JsonPropertyName("received")] decimal Received,
        [property: JsonPropertyName("spent")] decimal Spent,
        [property: JsonPropertyName("advancesOut")] decimal AdvancesOut,
        [property: JsonPropertyName("balance")] decimal Balance);

    private sealed record TransferRow(
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("transfer_date")] string? TransferDate);

    private sealed record FundPositionDto(
        [property: JsonPropertyName("cash")] FundSideDto Cash,
        [property: JsonPropertyName("bank")] FundSideDto Bank,
        // Bundled in rather than served from /api/fund_transfers so the
        // dashboard costs one request here instead of two.
        [property: JsonPropertyName("recentTransfers")] IReadOnlyList<TransferRow> RecentTransfers,
        [property: JsonPropertyName("total")] TotalDto Total);

    private sealed record EntryDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("amount")] decimal Amount);

    private sealed record BreakdownDto(
        [property: JsonPropertyName("cash")] IReadOnlyList<EntryDto> Cash,
        [property: JsonPropertyName("bank")] IReadOnlyList<EntryDto> Bank);
}
