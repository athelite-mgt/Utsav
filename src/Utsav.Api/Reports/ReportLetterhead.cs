using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Utsav.Api.Endpoints;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Storage;

namespace Utsav.Api.Reports;

/// <summary>
/// What a committee's documents say about the committee: its name, city, brand
/// colour and logo. This is the branding that matters most, because these are
/// the documents a committee hands to auditors and donors, and they must read
/// as the committee's own. The product's name appears nowhere on them.
///
/// Built from the caller's own committee only, and degrading rather than
/// failing: an unreadable or undecodable logo means no logo, never a report
/// that cannot be generated.
/// </summary>
internal sealed record ReportLetterhead(string CommitteeName, string? City, string? AccentColour, byte[]? Logo)
{
    public static ReportLetterhead Plain(string committeeName) => new(committeeName, null, null, null);

    /// <summary>"Barrackpore · Financial Statements — 2026", or without the city if there isn't one.</summary>
    public string Caption(string subtitle) => string.IsNullOrWhiteSpace(City) ? subtitle : $"{City} · {subtitle}";

    /// <summary>A validated <c>#rrggbb</c>, or null: a bad stored colour must never fail a report.</summary>
    public string? AccentHex
    {
        get
        {
            if (string.IsNullOrEmpty(AccentColour) || !BrandingValidation.IsValidAccentColour(AccentColour))
            {
                return null;
            }

            var hex = AccentColour.ToLowerInvariant();
            return hex.Length == 4 ? "#" + string.Concat(hex.Skip(1).Select(c => $"{c}{c}")) : hex;
        }
    }

    /// <summary>
    /// Keeps a logo only if the PDF renderer can really decode it. A file that
    /// passes the signature check but is not a drawable picture would otherwise
    /// throw at render time and take the whole report down with it. GIF is
    /// dropped because the renderer cannot draw it.
    /// </summary>
    public static byte[]? SafeLogo(byte[]? bytes)
    {
        if (bytes is not { Length: > 0 } || ImageKind.Detect(bytes) is not { } kind || kind.Extension == "gif")
        {
            return null;
        }

        try
        {
            using var decoded = QuestPDF.Infrastructure.Image.FromBinaryData(bytes);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The caller's own committee's letterhead. The logo is read only from its own prefix.</summary>
    public static async Task<ReportLetterhead> LoadAsync(UtsavDbContext db, IBlobStorageService storage, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == db.TenantId)
            .Select(o => new { o.Name, o.City, o.AccentColour, o.LogoBlob })
            .SingleAsync(ct);

        byte[]? logo = null;
        if (org.LogoBlob is not null && storage.IsConfigured
            && org.LogoBlob.StartsWith(BrandingUrls.BlobPrefix(db.TenantId), StringComparison.Ordinal))
        {
            try
            {
                await using var download = (await storage.DownloadAsync(org.LogoBlob, ct)).Content;
                using var buffer = new MemoryStream();
                await download.CopyToAsync(buffer, ct);
                logo = SafeLogo(buffer.ToArray());
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // A missing or unreadable logo is not a reason to fail a report.
            }
        }

        return new ReportLetterhead(org.Name, org.City, org.AccentColour, logo);
    }

    /// <summary>
    /// The page header: logo, committee name, a caption, and a rule in the
    /// committee's own colour. <paramref name="subtitle"/> null means name and
    /// city only (for reports that print their own title beneath).
    /// </summary>
    public void ComposeHeader(IContainer container, string? subtitle, float nameSize = 16, bool withRule = true)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (Logo is not null)
                {
                    row.ConstantItem(nameSize * 3.2f).PaddingRight(10).Height(nameSize * 2.6f).Image(Logo).FitArea();
                }

                row.RelativeItem().Column(text =>
                {
                    text.Item().Text(CommitteeName).FontSize(nameSize).Bold();
                    var caption = subtitle is null ? City : Caption(subtitle);
                    if (!string.IsNullOrWhiteSpace(caption))
                    {
                        text.Item().Text(caption).FontSize(nameSize <= 14 ? 9 : 10).FontColor(Colors.Grey.Darken2);
                    }
                });
            });

            if (withRule)
            {
                var rule = column.Item().PaddingTop(6).LineHorizontal(AccentHex is null ? 1f : 1.6f);
                rule.LineColor(AccentHex is { } hex ? Color.FromHex(hex) : Colors.Grey.Lighten1);
            }
        });
    }
}
