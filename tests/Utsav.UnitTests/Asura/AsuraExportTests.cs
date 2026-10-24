using ClosedXML.Excel;
using Utsav.Api.Asura;
using Utsav.Api.Reports;
using Utsav.UnitTests.Reports;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// Renders the exports for real and inspects the bytes. A PDF or spreadsheet
/// that throws, or that comes back as a valid-but-empty file, is the kind of
/// failure nobody notices until a treasurer opens the download in front of the
/// committee.
/// </summary>
public class AsuraExportTests
{
    private static readonly AsuraTool Tool = ToolCatalogue.Tools["collections_by_zone"];

    private static readonly ReportLetterhead Head = new("Barrackpore Kali Puja", "Barrackpore", "#7f1d1d", null);

    /// <summary>
    /// Deliberately includes a Bengali name and a null: the Bengali exercises
    /// the embedded font (Azure's image has no Indic fonts, so a system
    /// default renders blank boxes), and the null exercises the formatter that
    /// would otherwise throw on Convert.ToString of a missing cell.
    /// </summary>
    private static QueryResult Sample() => new(
        Columns: ["zone_id", "zone", "donors", "collected", "pct_of_projection"],
        Rows:
        [
            new Dictionary<string, object?>
            {
                ["zone_id"] = Guid.NewGuid(),
                ["zone"] = "শ্যামবাজার উত্তর",
                ["donors"] = 42,
                ["collected"] = 126_000m,
                ["pct_of_projection"] = 41.3m,
            },
            new Dictionary<string, object?>
            {
                ["zone_id"] = Guid.NewGuid(),
                ["zone"] = "Baghbazar",
                ["donors"] = 0,
                ["collected"] = null,
                ["pct_of_projection"] = null,
            },
        ],
        Truncated: false,
        ElapsedMs: 12);

    [Fact]
    public void Pdf_renders_a_real_document()
    {
        var bytes = AsuraExport.Pdf(Tool, Sample(), "Which zones are behind?", Head);

        // %PDF- magic number, and big enough to actually contain a table
        // rather than being an empty single page.
        Assert.True(bytes.Length > 2_000, $"PDF suspiciously small: {bytes.Length} bytes");
        Assert.Equal("%PDF-"u8.ToArray(), bytes[..5]);
    }

    [Fact]
    public void Pdf_survives_null_cells_and_bengali_text()
    {
        // Both of these threw or silently produced blank boxes in earlier
        // report work in this repo, so they are pinned here.
        var exception = Record.Exception(() => AsuraExport.Pdf(Tool, Sample(), null, Head));
        Assert.Null(exception);
    }

    [Fact]
    public void Excel_writes_numbers_as_numbers_not_text()
    {
        // The point of offering Excel rather than a screenshot: a treasurer has
        // to be able to sum and pivot the column. A spreadsheet of strings
        // looks identical and is useless.
        var bytes = AsuraExport.Excel(Tool, Sample(), "Which zones are behind?", Head);

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheets.First();

        var header = sheet.Row(7);
        var zoneColumn = Enumerable.Range(1, 10)
            .First(c => header.Cell(c).GetString().Equals("Zone", StringComparison.OrdinalIgnoreCase));
        var collectedColumn = Enumerable.Range(1, 10)
            .First(c => header.Cell(c).GetString().Equals("Collected", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("শ্যামবাজার উত্তর", sheet.Row(8).Cell(zoneColumn).GetString());

        var collected = sheet.Row(8).Cell(collectedColumn);
        Assert.Equal(XLDataType.Number, collected.DataType);
        Assert.Equal(126_000d, collected.GetDouble());
    }

    [Fact]
    public void Excel_carries_the_provenance_line()
    {
        // A download detached from the app is exactly where "where did this
        // number come from?" gets asked, so the answer ships inside the file.
        var bytes = AsuraExport.Excel(Tool, Sample(), "Which zones are behind?", Head);

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var provenance = workbook.Worksheets.First().Cell(5, 1).GetString();

        Assert.Contains("subscriptions", provenance);
        Assert.Contains("zones", provenance);
    }

    [Fact]
    public void Excel_sheet_name_stays_within_the_format_limit()
    {
        // Excel refuses a sheet name over 31 characters or containing : \ / ? * [ ]
        // by failing to open the file at all, so every tool title is checked.
        foreach (var tool in ToolCatalogue.Tools.Values)
        {
            var bytes = AsuraExport.Excel(tool, Sample(), null, Head);
            using var ms = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(ms);
            var name = workbook.Worksheets.First().Name;

            Assert.True(name.Length <= 31, $"{tool.Id}: sheet name '{name}' is {name.Length} chars");
            Assert.True(
                name.IndexOfAny(['\\', '/', ':', '*', '?', '[', ']']) < 0,
                $"{tool.Id}: sheet name '{name}' contains a character Excel rejects");
        }
    }

    [Fact]
    public void Excel_opens_with_the_committees_own_name_and_city()
    {
        var bytes = AsuraExport.Excel(Tool, Sample(), null, Head);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.First();

        Assert.Equal("Barrackpore Kali Puja", sheet.Cell(1, 1).GetString());
        Assert.Equal("Barrackpore", sheet.Cell(2, 1).GetString());
        // The report title still follows, and nothing in the first rows names the product.
        Assert.Equal(Tool.Title, sheet.Cell(3, 1).GetString());
        for (var row = 1; row <= 5; row++)
        {
            Assert.DoesNotContain("Utsav", sheet.Cell(row, 1).GetString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Pdf_with_a_logo_embeds_the_image_and_without_one_does_not()
    {
        var withLogo = new ReportLetterhead("Barrackpore Kali Puja", "Barrackpore", "#7f1d1d", TestImages.Png());

        var plain = AsuraExport.Pdf(Tool, Sample(), null, Head);
        var branded = AsuraExport.Pdf(Tool, Sample(), null, withLogo);

        Assert.True(CountImages(branded) > CountImages(plain), "the logo was not embedded in the PDF");
    }

    private static int CountImages(byte[] pdf)
    {
        var text = System.Text.Encoding.Latin1.GetString(pdf);
        return System.Text.RegularExpressions.Regex.Matches(text, @"/Subtype\s*/Image").Count;
    }
}
