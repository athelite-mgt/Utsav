using Utsav.Api.Reports;

namespace Utsav.UnitTests.Reports;

public class ReportLetterheadTests
{
    [Fact]
    public void The_caption_names_the_city_when_there_is_one()
    {
        var head = new ReportLetterhead("Barrackpore Kali Puja", "Barrackpore", null, null);

        Assert.Equal("Barrackpore · Financial Statements — 2026", head.Caption("Financial Statements — 2026"));
    }

    [Fact]
    public void The_caption_has_no_dangling_separator_without_a_city()
    {
        var head = new ReportLetterhead("A Committee", null, null, null);

        Assert.Equal("Financial Statements — 2026", head.Caption("Financial Statements — 2026"));
        Assert.DoesNotContain("·", head.Caption("x"));
    }

    [Fact]
    public void A_committees_documents_never_carry_the_product_or_another_festivals_name()
    {
        // These go to auditors and donors: they must read as the committee's
        // own, not as output from a product it happens to use.
        var head = new ReportLetterhead("Barrackpore Kali Puja", "Barrackpore", "#7f1d1d", null);

        Assert.DoesNotContain("Utsav", head.CommitteeName + head.Caption("Statements"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Durga", head.CommitteeName + head.Caption("Statements"), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("#b45309", "#b45309")]
    [InlineData("#FA0", "#ffaa00")]
    public void A_valid_brand_colour_becomes_a_pdf_colour(string stored, string expected)
    {
        var head = new ReportLetterhead("X", null, stored, null);

        Assert.Equal(expected, head.AccentHex);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff;} x")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_means_no_colour_rather_than_a_failed_report(string? stored)
    {
        Assert.Null(new ReportLetterhead("X", null, stored, null).AccentHex);
    }

    [Fact]
    public void A_decodable_png_is_kept_as_the_logo()
    {
        var png = TestImages.Png();

        Assert.Equal(png, ReportLetterhead.SafeLogo(png));
    }

    [Fact]
    public void A_logo_that_cannot_be_decoded_is_dropped_rather_than_failing_the_report()
    {
        // Passes the signature check yet is not a real picture. Handed to the
        // PDF renderer it would throw and take the whole audit report with it.
        byte[] headerOnly = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

        Assert.Null(ReportLetterhead.SafeLogo(headerOnly));
    }

    [Theory]
    [InlineData("GIF89a\x01\x00\x01\x00")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'/>")]
    [InlineData("plain text")]
    public void Formats_the_pdf_renderer_cannot_draw_are_dropped(string content)
    {
        Assert.Null(ReportLetterhead.SafeLogo(System.Text.Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void No_logo_in_means_no_logo_out()
    {
        Assert.Null(ReportLetterhead.SafeLogo(null));
        Assert.Null(ReportLetterhead.SafeLogo([]));
    }

    [Fact]
    public void A_plain_letterhead_is_usable_with_just_a_name()
    {
        var plain = ReportLetterhead.Plain("Some Committee");

        Assert.Equal("Some Committee", plain.CommitteeName);
        Assert.Null(plain.Logo);
        Assert.Null(plain.AccentHex);
    }
}
