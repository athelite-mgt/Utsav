using System.Text.RegularExpressions;

namespace Utsav.Domain.Finance;

/// <summary>Which side of the fund a payment landed on.</summary>
public enum FundSideKind
{
    /// <summary>The physical cash box.</summary>
    Cash,

    /// <summary>The bank / online account.</summary>
    Bank,
}

/// <summary>
/// Ported verbatim from <c>server/paymentMode.mjs</c> — the behaviour here must
/// not drift, because it decides how every historical row is bucketed on the
/// dashboard's cash-vs-bank split.
///
/// The committee holds money in two places, and every income or expense row
/// records a free-text <c>mode</c> drawn from an editable lookup list. Rather
/// than pin this to the exact labels seeded today ("Online - Gpay", "Cheque",
/// "Bank Transfer"), it matches on the words that appear in any sensible
/// payment-method name, so a committee that later adds "UPI" or "PhonePe" to
/// the lookup doesn't silently get it counted as cash.
///
/// Anything unrecognised — including blank, which is the overwhelming majority
/// of historical rows — counts as CASH. That is deliberate: a puja committee's
/// default is a note in a tin, and treating unlabelled money as cash keeps the
/// two balances adding up to the true total.
/// </summary>
public static partial class PaymentModeClassifier
{
    [GeneratedRegex(
        @"(online|g[\s-]?pay|upi|phonepe|paytm|bhim|net[\s-]?bank|bank|neft|imps|rtgs|cheque|check|dd|demand draft|card|transfer)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OnlinePattern();

    public static bool IsOnline(string? mode) =>
        mode is not null && OnlinePattern().IsMatch(mode);

    public static FundSideKind BucketOf(string? mode) =>
        IsOnline(mode) ? FundSideKind.Bank : FundSideKind.Cash;
}
