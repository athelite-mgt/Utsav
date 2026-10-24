using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Reports;
using Utsav.UnitTests.Reports;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// The documents a committee hands to auditors and donors carry that
/// committee's own letterhead and nobody else's, and a bad logo can never stop
/// a report being produced. Logo state is set up and torn down inside each test
/// (committee C), so tests do not depend on each other.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class LetterheadTests(DatabaseFixture fixture) : IAsyncLifetime
{
    // Everything a test puts in shared state, undone afterwards so no other
    // test (several count blobs and tokens exactly) sees these.
    private readonly List<string> _blobs = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var blob in _blobs)
        {
            await fixture.Blobs.DeleteAsync(blob);
        }

        await using var identity = fixture.ContextWithNoTenant();
        await identity.Organizations.Where(o => o.Id == fixture.TenantC)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LogoBlob, (string?)null));

        await using var inC = fixture.ContextFor(fixture.TenantC);
        await inC.FeastTokens.ExecuteDeleteAsync();
    }

    private static int ImageCount(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Subtype\s*/Image").Count;

    private async Task SetLogoAsync(Guid org, byte[]? png, string? nameOverride = null)
    {
        string? blob = null;
        if (png is not null)
        {
            blob = nameOverride ?? $"{org}/branding/test-{Guid.NewGuid():N}.png";
            _blobs.Add(blob);
            await fixture.Blobs.UploadAsync(new MemoryStream(png), blob, "image/png");
        }

        await using var identity = fixture.ContextWithNoTenant();
        await identity.Organizations.Where(o => o.Id == org).ExecuteUpdateAsync(s => s.SetProperty(o => o.LogoBlob, blob));
    }

    private async Task<byte[]> AuditPdfAsync(string token, Guid year)
    {
        using var client = fixture.ClientFor(token);
        var response = await client.GetAsync($"/api/reports/audit.pdf?year_id={year}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsByteArrayAsync();
    }

    [Fact]
    public async Task A_letterhead_is_built_from_the_callers_own_committee_only()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);
        await using var asB = fixture.ContextFor(fixture.TenantB);

        var a = await ReportLetterhead.LoadAsync(asA, fixture.Blobs, default);
        var b = await ReportLetterhead.LoadAsync(asB, fixture.Blobs, default);

        Assert.Equal("Durga Committee", a.CommitteeName);
        Assert.Equal("Kali Committee", b.CommitteeName);
        Assert.Equal("Barrackpore", a.City);
    }

    [Fact]
    public async Task A_committees_logo_appears_on_its_letterhead_and_not_on_anyone_elses()
    {
        var png = TestImages.Png();
        await SetLogoAsync(fixture.TenantC, png);
        try
        {
            await using var asC = fixture.ContextFor(fixture.TenantC);
            await using var asA = fixture.ContextFor(fixture.TenantA);

            Assert.Equal(png, (await ReportLetterhead.LoadAsync(asC, fixture.Blobs, default)).Logo);
            Assert.Null((await ReportLetterhead.LoadAsync(asA, fixture.Blobs, default)).Logo);
        }
        finally
        {
            await SetLogoAsync(fixture.TenantC, null);
        }
    }

    [Fact]
    public async Task A_row_pointing_at_another_committees_file_yields_no_logo_rather_than_their_file()
    {
        // Same boundary as documents and the public logo route: bytes are
        // fetched by a stored path, which must lie inside the committee's own
        // branding prefix.
        var theirs = $"{fixture.TenantB}/branding/their-logo.png";
        _blobs.Add(theirs);
        await fixture.Blobs.UploadAsync(new MemoryStream(TestImages.Png(r: 1, g: 2, b: 3)), theirs, "image/png");
        await SetLogoAsync(fixture.TenantC, TestImages.Png(), nameOverride: theirs);
        try
        {
            await using var asC = fixture.ContextFor(fixture.TenantC);

            Assert.Null((await ReportLetterhead.LoadAsync(asC, fixture.Blobs, default)).Logo);
        }
        finally
        {
            await SetLogoAsync(fixture.TenantC, null);
        }
    }

    [Fact]
    public async Task The_audit_pdf_carries_the_committees_logo_when_it_has_one()
    {
        await SetLogoAsync(fixture.TenantC, TestImages.Png());
        try
        {
            var branded = await AuditPdfAsync(fixture.TokenForC, fixture.C.Year);
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(branded.AsSpan(0, 4)));

            await SetLogoAsync(fixture.TenantC, null);
            var plain = await AuditPdfAsync(fixture.TokenForC, fixture.C.Year);

            Assert.True(ImageCount(branded) > ImageCount(plain), "the logo was not embedded in the audit PDF");
        }
        finally
        {
            await SetLogoAsync(fixture.TenantC, null);
        }
    }

    [Fact]
    public async Task A_logo_that_looks_like_an_image_but_is_not_cannot_stop_the_audit_report()
    {
        // Passes the signature check (so it was accepted as an upload) yet is
        // not a drawable picture. The report must still come out.
        byte[] headerOnly = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
        await SetLogoAsync(fixture.TenantC, headerOnly);
        try
        {
            var pdf = await AuditPdfAsync(fixture.TokenForC, fixture.C.Year);

            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.AsSpan(0, 4)));
        }
        finally
        {
            await SetLogoAsync(fixture.TenantC, null);
        }
    }

    [Fact]
    public async Task A_logo_whose_file_has_gone_missing_cannot_stop_the_audit_report()
    {
        await using (var identity = fixture.ContextWithNoTenant())
        {
            await identity.Organizations.Where(o => o.Id == fixture.TenantC)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.LogoBlob, $"{fixture.TenantC}/branding/vanished.png"));
        }

        try
        {
            var pdf = await AuditPdfAsync(fixture.TokenForC, fixture.C.Year);

            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.AsSpan(0, 4)));
        }
        finally
        {
            await SetLogoAsync(fixture.TenantC, null);
        }
    }

    [Fact]
    public async Task Printed_feast_tokens_carry_the_committees_logo()
    {
        await SetLogoAsync(fixture.TenantC, TestImages.Png());
        try
        {
            using var asC = fixture.ClientFor(fixture.TokenForC);
            var response = await asC.PostAsJsonAsync("/api/feast_tokens/print", new { year_id = fixture.C.Year.ToString(), count = 2 });
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

            var pdf = await response.Content.ReadAsByteArrayAsync();
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf.AsSpan(0, 4)));
            // The QR codes are images too, so a logo shows up as MORE of them.
            Assert.True(ImageCount(pdf) >= 3, $"expected a logo as well as two QR codes, found {ImageCount(pdf)} images");
        }
        finally
        {
            await SetLogoAsync(fixture.TenantC, null);
        }
    }
}
