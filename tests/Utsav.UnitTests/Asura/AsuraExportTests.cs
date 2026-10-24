using ClosedXML.Excel;
using Utsav.Api.Asura;

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
        var bytes = AsuraExport.Pdf(Tool, Sample(), "Which zones are behind?");

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
        var exception = Record.Exception(() => AsuraExport.Pdf(Tool, Sample(), null));
        Assert.Null(exception);
    }

    [Fact]
    public void Excel_writes_numbers_as_numbers_not_text()
    {
        // The point of offering Excel rather than a screenshot: a treasurer has
        // to be able to sum and pivot the column. A spreadsheet of strings
        // looks identical and is useless.
        var bytes = AsuraExport.Excel(Tool, Sample(), "Which zones are behind?");

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var sheet = workbook.Worksheets.First();

        var header = sheet.Row(5);
        var zoneColumn = Enumerable.Range(1, 10)
            .First(c => header.Cell(c).GetString().Equals("Zone", StringComparison.OrdinalIgnoreCase));
        var collectedColumn = Enumerable.Range(1, 10)
            .First(c => header.Cell(c).GetString().Equals("Collected", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("শ্যামবাজার উত্তর", sheet.Row(6).Cell(zoneColumn).GetString());

        var collected = sheet.Row(6).Cell(collectedColumn);
        Assert.Equal(XLDataType.Number, collected.DataType);
        Assert.Equal(126_000d, collected.GetDouble());
    }

    [Fact]
    public void Excel_carries_the_provenance_line()
    {
        // A download detached from the app is exactly where "where did this
        // number come from?" gets asked, so the answer ships inside the file.
        var bytes = AsuraExport.Excel(Tool, Sample(), "Which zones are behind?");

        using var ms = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(ms);
        var provenance = workbook.Worksheets.First().Cell(3, 1).GetString();

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
            var bytes = AsuraExport.Excel(tool, Sample(), null);
            using var ms = new MemoryStream(bytes);
            using var workbook = new XLWorkbook(ms);
            var name = workbook.Worksheets.First().Name;

            Assert.True(name.Length <= 31, $"{tool.Id}: sheet name '{name}' is {name.Length} chars");
            Assert.True(
                name.IndexOfAny(['\\', '/', ':', '*', '?', '[', ']']) < 0,
                $"{tool.Id}: sheet name '{name}' contains a character Excel rejects");
        }
    }
}
