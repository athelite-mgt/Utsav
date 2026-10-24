using System.Globalization;
using System.Reflection;
using ClosedXML.Excel;
using Utsav.Api.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Utsav.Api.Asura;

/// <summary>
/// Renders an Asura report as a PDF or a spreadsheet, server-side.
///
/// <para>
/// Both formats are produced from a freshly executed report rather than from
/// rows posted by the browser. A download is the artefact that gets printed,
/// filed and argued over at a committee meeting, so it has to be what the
/// database says — not what a stale tab is still showing, and not something a
/// crafted request could substitute.
/// </para>
/// </summary>
internal static class AsuraExport
{
    private const string FontFamily = "Noto Sans Bengali";
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");
    private static bool _fontsRegistered;
    private static readonly Lock FontLock = new();

    /// <summary>
    /// Prepares QuestPDF: licence tier, then fonts.
    ///
    /// <para>
    /// The licence is declared here as well as in <c>Program.cs</c>, and
    /// deliberately so. It is a static that must be set before the first
    /// render, which meant this renderer only worked when hosted inside the
    /// API — calling it from anywhere else (a unit test, a future background
    /// job, a CLI export) threw "Please configure the QuestPDF license". A
    /// rendering component that silently depends on someone else's startup
    /// having run is fragile, and the failure arrives at render time rather
    /// than at startup. Setting it where it is needed is idempotent and the
    /// same Community tier Program.cs declares.
    /// </para>
    ///
    /// <para>
    /// Fonts: same reasoning as <see cref="Reports.AuditReportPdf"/> — the
    /// Azure App Service image ships no Indic fonts, so Bengali donor names
    /// would render as blank boxes against any system default. The TTFs are
    /// embedded resources so they travel with the assembly.
    /// </para>
    /// </summary>
    private static void EnsureFonts()
    {
        if (_fontsRegistered)
        {
            return;
        }

        lock (FontLock)
        {
            if (_fontsRegistered)
            {
                return;
            }

            QuestPDF.Settings.License = LicenseType.Community;

            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames()
                         .Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = asm.GetManifestResourceStream(name);
                if (stream is not null)
                {
                    QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
                }
            }

            _fontsRegistered = true;
        }
    }

    public static byte[] Pdf(AsuraTool tool, QueryResult result, string? question, ReportLetterhead letterhead)
    {
        EnsureFonts();

        // Identifier columns exist to carry a drill-down, not to be read.
        var columns = result.Columns.Where(c => !c.EndsWith("_id", StringComparison.OrdinalIgnoreCase)).ToList();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                // Landscape: these reports are wide (zone, collector, four
                // money columns and a percentage), and portrait forces a font
                // size nobody can read across a committee table.
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(t => t.FontFamily(FontFamily).FontSize(8));

                page.Header().Column(header =>
                {
                    // Whose report this is comes first: it travels to auditors
                    // and committee members with nothing else to place it.
                    header.Item().PaddingBottom(6).Element(c => letterhead.ComposeHeader(c, subtitle: null, nameSize: 12, withRule: false));
                    header.Item().Text(tool.Title).FontSize(15).SemiBold();
                    if (!string.IsNullOrWhiteSpace(question))
                    {
                        header.Item().PaddingTop(2).Text($"Asked: {question}")
                            .FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                    }

                    // Provenance travels with the document. A PDF detached from
                    // the app is exactly where "where did this number come
                    // from?" gets asked, and the answer should be on the page.
                    header.Item().PaddingTop(2).Text(
                            $"Generated {DateTime.UtcNow.ToString("dd MMM yyyy HH:mm", Inr)} UTC · "
                            + $"{result.Rows.Count:N0} rows{(result.Truncated ? " (capped)" : "")} · "
                            + $"source: {string.Join(", ", AsuraOrchestrator.TablesIn(tool.Sql))}")
                        .FontSize(7).FontColor(Colors.Grey.Darken1);
                    header.Item().PaddingTop(6).LineHorizontal(0.6f).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingVertical(8).Table(table =>
                {
                    table.ColumnsDefinition(def =>
                    {
                        foreach (var _ in columns)
                        {
                            def.RelativeColumn();
                        }
                    });

                    table.Header(h =>
                    {
                        foreach (var column in columns)
                        {
                            h.Cell().Background(Colors.Grey.Lighten3).Padding(3)
                                .Text(Humanise(column)).SemiBold().FontSize(7.5f);
                        }
                    });

                    var striped = false;
                    foreach (var row in result.Rows)
                    {
                        striped = !striped;
                        foreach (var column in columns)
                        {
                            var value = row.GetValueOrDefault(column);
                            var cell = table.Cell().Padding(3);
                            if (striped)
                            {
                                cell = cell.Background(Colors.Grey.Lighten5);
                            }

                            // Numbers right-aligned so columns of rupees line
                            // up and can be scanned down.
                            if (IsNumber(value))
                            {
                                cell = cell.AlignRight();
                            }

                            cell.Text(Cell(value)).FontSize(7.5f);
                        }
                    }
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Utsav · Asura · ").FontSize(7).FontColor(Colors.Grey.Darken1);
                    t.CurrentPageNumber().FontSize(7);
                    t.Span(" / ").FontSize(7);
                    t.TotalPages().FontSize(7);
                });
            });
        }).GeneratePdf();
    }

    public static byte[] Excel(AsuraTool tool, QueryResult result, string? question, ReportLetterhead letterhead)
    {
        using var workbook = new XLWorkbook();

        // Excel caps sheet names at 31 characters and rejects : \ / ? * [ ]
        var sheetName = new string(tool.Title.Where(c => !"\\/:*?[]".Contains(c)).ToArray());
        var sheet = workbook.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);

        var columns = result.Columns.ToList();

        // Rows 1-2: whose report this is. Fixed positions (the city row is left
        // empty rather than omitted) so every sheet has the same layout.
        sheet.Cell(1, 1).Value = letterhead.CommitteeName;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        if (!string.IsNullOrWhiteSpace(letterhead.City))
        {
            sheet.Cell(2, 1).Value = letterhead.City;
            sheet.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        }

        sheet.Cell(3, 1).Value = tool.Title;
        sheet.Cell(3, 1).Style.Font.Bold = true;
        sheet.Cell(3, 1).Style.Font.FontSize = 13;

        sheet.Cell(4, 1).Value = string.IsNullOrWhiteSpace(question) ? tool.Question : $"Asked: {question}";
        sheet.Cell(5, 1).Value =
            $"Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC · {result.Rows.Count:N0} rows"
            + (result.Truncated ? " (capped)" : "")
            + $" · source: {string.Join(", ", AsuraOrchestrator.TablesIn(tool.Sql))}";
        sheet.Cell(5, 1).Style.Font.FontSize = 9;
        sheet.Cell(5, 1).Style.Font.FontColor = XLColor.Gray;

        const int headerRow = 7;
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = Humanise(columns[c]);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEE, 0xEA, 0xF6);
        }

        for (var r = 0; r < result.Rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                var value = result.Rows[r].GetValueOrDefault(columns[c]);
                var cell = sheet.Cell(headerRow + 1 + r, c + 1);

                // Written as real numbers and dates, not strings. A
                // spreadsheet nobody can sum or pivot is a screenshot with
                // extra steps, which defeats the point of offering Excel.
                switch (value)
                {
                    case null:
                        break;
                    case decimal d:
                        cell.Value = d;
                        cell.Style.NumberFormat.Format = "#,##0.##";
                        break;
                    case double dbl:
                        cell.Value = dbl;
                        cell.Style.NumberFormat.Format = "#,##0.##";
                        break;
                    case int i:
                        cell.Value = i;
                        break;
                    case long l:
                        cell.Value = l;
                        break;
                    case bool b:
                        cell.Value = b;
                        break;
                    case DateTime dt:
                        cell.Value = dt;
                        cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                        break;
                    default:
                        cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                        break;
                }
            }
        }

        if (result.Rows.Count > 0)
        {
            var range = sheet.Range(headerRow, 1, headerRow + result.Rows.Count, columns.Count);
            range.SetAutoFilter();
            sheet.SheetView.FreezeRows(headerRow);
        }

        sheet.Columns().AdjustToContents(8d, 48d);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static bool IsNumber(object? value) =>
        value is decimal or double or float or int or long or short;

    private static string Cell(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("#,##0.##", Inr),
        double d => d.ToString("#,##0.##", Inr),
        int i => i.ToString("#,##0", Inr),
        long l => l.ToString("#,##0", Inr),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static string Humanise(string column) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(column.Replace('_', ' '));
}
