using System.Globalization;
using System.Reflection;
using Utsav.Infrastructure.Data.Entities;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Utsav.Api.Reports;

/// <summary>
/// Renders a batch of freshly-printed, still-blank <see cref="FeastToken"/>
/// rows as a sheet of physical coupons — ten per A4 page, each carrying the
/// committee name, the puja year, today's date, a QR code, and the
/// human-readable code underneath it (the manual-entry fallback for a gate
/// volunteer whose camera/scanner isn't working). Printed before any donor is
/// known: assignment to a <see cref="Feast"/> record happens later, door to
/// door, so nothing donor-specific appears here.
/// </summary>
internal static class FeastTokenSheetPdf
{
    private const string FontFamily = "Noto Sans Bengali";
    private const string Maroon = "#7C3A33";
    private const int TokensPerPage = 10;
    private static bool _fontsRegistered;
    private static readonly Lock FontLock = new();

    /// <summary>Same embedded-font workaround as <see cref="AuditReportPdf"/> — the Azure App Service image ships no Indic fonts.</summary>
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

    internal static byte[] Render(IReadOnlyList<FeastToken> tokens, string orgName, int pujaYear, DateTime printedOnUtc)
    {
        EnsureFonts();

        var printedOn = printedOnUtc.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

        return QuestPDF.Fluent.Document.Create(doc =>
        {
            // Chunked rather than left to the table's own pagination — the
            // requirement is exactly ten per page, not "however many fit",
            // and the last page is simply whatever remains.
            foreach (var pageTokens in tokens.Chunk(TokensPerPage))
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(t => t.FontFamily(FontFamily));

                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        foreach (var token in pageTokens)
                        {
                            table.Cell().Element(c => Coupon(c, token, orgName, pujaYear, printedOn));
                        }
                    });
                });
            }
        }).GeneratePdf();
    }

    private static void Coupon(IContainer container, FeastToken token, string orgName, int pujaYear, string printedOn) =>
        container
            .Padding(5)
            .Height(150)
            .Border(1)
            .BorderColor(Colors.Grey.Medium)
            .Padding(10)
            .Column(col =>
            {
                col.Spacing(4);
                col.Item().Text(orgName).FontSize(12).Bold().FontColor(Maroon);
                col.Item().Text($"Feast {pujaYear} · {printedOn}").FontSize(8).FontColor(Colors.Grey.Darken2);

                col.Item().PaddingTop(2).Row(row =>
                {
                    row.ConstantItem(72).Height(72).Image(GenerateQrPng(token.Code));
                    row.RelativeItem().PaddingLeft(10).Column(inner =>
                    {
                        inner.Spacing(2);
                        inner.Item().Text(token.Code).FontSize(17).Bold();
                        inner.Item().Text("Entry Token").FontSize(8).FontColor(Colors.Grey.Darken1);
                        inner.Item().Text("Present this coupon at the feast entrance").FontSize(7).FontColor(Colors.Grey.Darken1);
                    });
                });
            });

    /// <summary>
    /// The code is the whole payload — a gate scan just needs to look it up,
    /// no embedded URL or metadata. <c>ECCLevel.Q</c> tolerates a torn or
    /// creased coupon corner better than the default while staying well
    /// within the module count a 72pt-square image can render legibly.
    /// </summary>
    private static byte[] GenerateQrPng(string code)
    {
        var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(12);
    }
}
