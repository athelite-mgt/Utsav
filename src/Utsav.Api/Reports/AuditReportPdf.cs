using System.Globalization;
using System.Reflection;
using Utsav.Api.Endpoints;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Utsav.Api.Reports;

/// <summary>
/// Renders the year-end statements as a PDF on the server, for the API-key
/// report export (see ApiKeyAuthenticationHandler) — the committee's Hermes
/// agent fetches this on a schedule, with no browser involved, so the
/// client-side jsPDF path the Reports page uses can't serve it.
///
/// The figures come from <see cref="AuditReportEndpoint.BuildAsync"/>,
/// the same call the JSON endpoint makes, so this is purely a rendering of
/// numbers computed once — the PDF and the on-screen report cannot disagree.
/// </summary>
internal static class AuditReportPdf
{
    private const string FontFamily = "Noto Sans Bengali";
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");
    private static bool _fontsRegistered;
    private static readonly Lock FontLock = new();

    /// <summary>
    /// The Azure App Service image ships no Indic fonts, so Bengali donor
    /// names and expense details would render as blank boxes against any
    /// system default — the same failure the browser export had before its
    /// font was embedded. These TTFs are embedded resources (see
    /// Utsav.Api.csproj) so they travel with the assembly.
    /// </summary>
    private static void EnsureFonts()
    {
        if (_fontsRegistered) return;
        lock (FontLock)
        {
            if (_fontsRegistered) return;
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null) QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
            }
            _fontsRegistered = true;
        }
    }

    internal static byte[] Render(AuditReportEndpoint.AuditReportDto r)
    {
        EnsureFonts();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().Text(r.OrgName).FontSize(16).Bold();
                    col.Item().Text($"Financial Statements — {r.YearLabel}").FontSize(10).FontColor(Colors.Grey.Darken2);
                    col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(8).Column(col =>
                {
                    col.Spacing(14);

                    TwoSided(col, "Receipts & Payments Account (cash basis)",
                        "Receipts", r.ReceiptsPayments.Receipts, r.ReceiptsPayments.TotalReceipts,
                        "Payments", r.ReceiptsPayments.Payments, r.ReceiptsPayments.TotalPayments);

                    TwoSided(col, "Income & Expenditure Account (accrual basis)",
                        "Income", r.IncomeExpenditure.Income, r.IncomeExpenditure.TotalIncome,
                        "Expenditure", r.IncomeExpenditure.Expenditure, r.IncomeExpenditure.TotalExpenditure);

                    var surplus = r.IncomeExpenditure.Surplus;
                    col.Item().Text($"{(surplus >= 0 ? "Surplus" : "Deficit")} for the year: {Money(Math.Abs(surplus))}").Bold();

                    TwoSided(col, $"Balance Sheet — as at close of {r.YearLabel}",
                        "Funds & Liabilities", r.BalanceSheet.Funds, r.BalanceSheet.TotalFunds,
                        "Assets", r.BalanceSheet.Assets, r.BalanceSheet.TotalAssets);

                    if (r.Notes.Count > 0)
                    {
                        col.Item().Column(n =>
                        {
                            n.Item().PaddingBottom(3).Text("Notes to the accounts").Bold();
                            foreach (var note in r.Notes)
                                n.Item().Text($"• {note}").FontSize(8).FontColor(Colors.Grey.Darken2);
                        });
                    }

                    IncomeAnnexure(col, "Annexure A — Membership", "Donor", r.Annexures.Membership, withZone: true);
                    IncomeAnnexure(col, "Annexure B — Special Donation", "Donor", r.Annexures.Special, withZone: true);
                    IncomeAnnexure(col, "Annexure C — Advertisements", "Advertiser", r.Annexures.Ads, withZone: false);
                    IncomeAnnexure(col, "Annexure D — Other Incomes", "Name", r.Annexures.Other, withZone: false);
                    ExpenseAnnexure(col, "Annexure E — Expenses", r.Annexures.Expenses);
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                    row.ConstantItem(70).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7).FontColor(Colors.Grey.Darken1));
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    /// <summary>A classic two-column account — particulars and amount on each side, totals on the closing row.</summary>
    private static void TwoSided(
        ColumnDescriptor col, string heading,
        string leftLabel, IReadOnlyList<AuditReportEndpoint.LineItem> left, decimal leftTotal,
        string rightLabel, IReadOnlyList<AuditReportEndpoint.LineItem> right, decimal rightTotal)
    {
        col.Item().Column(section =>
        {
            section.Item().PaddingBottom(4).Text(heading).FontSize(11).Bold();
            section.Item().Row(row =>
            {
                row.RelativeItem().Element(c => Side(c, leftLabel, left, leftTotal));
                row.ConstantItem(12);
                row.RelativeItem().Element(c => Side(c, rightLabel, right, rightTotal));
            });
        });
    }

    private static void Side(IContainer container, string label, IReadOnlyList<AuditReportEndpoint.LineItem> lines, decimal total) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); });

            table.Header(h =>
            {
                h.Cell().Element(HeadCell).Text(label);
                h.Cell().Element(HeadCell).AlignRight().Text("Amount");
            });

            foreach (var l in lines)
            {
                table.Cell().Element(BodyCell).Text(l.Label);
                table.Cell().Element(BodyCell).AlignRight().Text(Money(l.Amount));
            }

            table.Cell().Element(TotalCell).Text("Total").Bold();
            table.Cell().Element(TotalCell).AlignRight().Text(Money(total)).Bold();
        });

    private static void IncomeAnnexure(
        ColumnDescriptor col, string heading, string nameLabel,
        IReadOnlyList<AuditReportEndpoint.IncomeAnnexureRow> rows, bool withZone)
    {
        col.Item().Column(section =>
        {
            section.Item().PaddingBottom(3).Text(heading).FontSize(11).Bold();
            section.Item().PaddingBottom(3)
                .Text($"{rows.Count} {(rows.Count == 1 ? "entry" : "entries")} · Billed total {Money(rows.Sum(x => x.Billed))}")
                .FontSize(8).FontColor(Colors.Grey.Darken2);

            if (rows.Count == 0)
            {
                section.Item().Text("Nothing recorded for this year.").FontSize(8).FontColor(Colors.Grey.Darken1);
                return;
            }

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(3);                 // name
                    if (withZone) c.RelativeColumn(2);   // zone
                    c.RelativeColumn(1.4f);              // bill no
                    c.RelativeColumn(1.6f);              // billed date
                    c.RelativeColumn(1.6f);              // billed
                    c.RelativeColumn(1.6f);              // collected date
                    c.RelativeColumn(1.6f);              // collected
                    c.RelativeColumn(1.4f);              // due
                    c.RelativeColumn(1.2f);              // mode
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeadCell).Text(nameLabel);
                    if (withZone) h.Cell().Element(HeadCell).Text("Zone");
                    h.Cell().Element(HeadCell).Text("Bill No");
                    h.Cell().Element(HeadCell).Text("Billed Date");
                    h.Cell().Element(HeadCell).AlignRight().Text("Billed");
                    h.Cell().Element(HeadCell).Text("Collected Date");
                    h.Cell().Element(HeadCell).AlignRight().Text("Collected");
                    h.Cell().Element(HeadCell).AlignRight().Text("Due");
                    h.Cell().Element(HeadCell).Text("Mode");
                });

                foreach (var x in rows)
                {
                    table.Cell().Element(BodyCell).Text(x.Name);
                    if (withZone) table.Cell().Element(BodyCell).Text(Dash(x.Zone));
                    table.Cell().Element(BodyCell).Text(Dash(x.BillNo));
                    table.Cell().Element(BodyCell).Text(Dash(x.BilledOn));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(x.Billed));
                    table.Cell().Element(BodyCell).Text(Dash(x.CollectedOn));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(x.Collected));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(x.Due));
                    table.Cell().Element(BodyCell).Text(Dash(x.Mode));
                }
            });
        });
    }

    private static void ExpenseAnnexure(
        ColumnDescriptor col, string heading, IReadOnlyList<AuditReportEndpoint.ExpenseAnnexureRow> rows)
    {
        col.Item().Column(section =>
        {
            section.Item().PaddingBottom(3).Text(heading).FontSize(11).Bold();
            section.Item().PaddingBottom(3)
                .Text($"{rows.Count} {(rows.Count == 1 ? "entry" : "entries")} · Total {Money(rows.Sum(x => x.Amount))}")
                .FontSize(8).FontColor(Colors.Grey.Darken2);

            if (rows.Count == 0)
            {
                section.Item().Text("Nothing recorded for this year.").FontSize(8).FontColor(Colors.Grey.Darken1);
                return;
            }

            section.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(2);     // category
                    c.RelativeColumn(3.5f);  // details
                    c.RelativeColumn(1.5f);  // voucher
                    c.RelativeColumn(1.6f);  // date
                    c.RelativeColumn(1.2f);  // mode
                    c.RelativeColumn(1.6f);  // amount
                });

                table.Header(h =>
                {
                    h.Cell().Element(HeadCell).Text("Category");
                    h.Cell().Element(HeadCell).Text("Details");
                    h.Cell().Element(HeadCell).Text("Voucher");
                    h.Cell().Element(HeadCell).Text("Date");
                    h.Cell().Element(HeadCell).Text("Mode");
                    h.Cell().Element(HeadCell).AlignRight().Text("Amount");
                });

                foreach (var x in rows)
                {
                    table.Cell().Element(BodyCell).Text(x.Category);
                    table.Cell().Element(BodyCell).Text(Dash(x.Details));
                    table.Cell().Element(BodyCell).Text(Dash(x.BillNo));
                    table.Cell().Element(BodyCell).Text(Dash(x.SpentOn));
                    table.Cell().Element(BodyCell).Text(Dash(x.Mode));
                    table.Cell().Element(BodyCell).AlignRight().Text(Money(x.Amount));
                }
            });
        });
    }

    private static IContainer HeadCell(IContainer c) => c
        .Background("#7C3A33").PaddingVertical(3).PaddingHorizontal(4)
        .DefaultTextStyle(t => t.FontColor(Colors.White).SemiBold().FontSize(8));

    private static IContainer BodyCell(IContainer c) => c
        .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
        .PaddingVertical(2.5f).PaddingHorizontal(4)
        .DefaultTextStyle(t => t.FontSize(8));

    private static IContainer TotalCell(IContainer c) => c
        .Background("#F8F6F3").BorderTop(1).BorderColor(Colors.Grey.Medium)
        .PaddingVertical(3).PaddingHorizontal(4)
        .DefaultTextStyle(t => t.FontSize(8));

    /// <summary>Indian digit grouping (1,00,000 not 100,000) — the format every figure on screen already uses.</summary>
    private static string Money(decimal v) => "₹ " + v.ToString("N0", Inr);

    private static string Dash(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v;
}
