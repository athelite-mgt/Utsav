using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Data;

/// <summary>
/// Brings a brand-new database up to a usable state for a committee that has
/// never run this app before.
///
/// The schema itself comes from EF migrations, so deploying for another puja is:
///
///   1. create an empty database
///   2. <c>dotnet ef database update</c>            (27 tables, 3 views, indexes)
///   3. start the API                                (this class does the rest)
///
/// Step 3 only ever fires against an empty <c>organizations</c> table, so it is
/// safe to leave enabled — restarting an established committee's API will not
/// touch their data, and will not quietly re-seed lookups an admin has since
/// renamed or deleted.
///
/// Everything here was previously hardcoded into schema-postgres.sql, including
/// the committee's own name. It is configuration now, because the point of this
/// class is that the next deployment is not Barrackpore.
/// </summary>
public sealed class CommitteeBootstrapper(
    UtsavDbContext db,
    IConfiguration configuration,
    ILogger<CommitteeBootstrapper> logger)
{
    private static readonly string[] Zones = ["16 Feet", "20 Feet", "14 Feet +"];

    private static readonly (string Category, string[] Labels)[] Lookups =
    [
        ("payment_mode", ["Cash", "Online - Gpay", "Cheque", "Bank Transfer", "Pending"]),
        // Shared by both expenses/budget and workflow tasks — one category
        // list, not two, so "how much did we spend on Decoration" and "what
        // Decoration tasks are still open" use the same word for the same
        // thing. The last six were merged in from what had been a separate,
        // workflow-only category list.
        ("expense_category", [
            "Siddhi", "IDOL", "Purohit", "Puja Essentials & Offerings", "Decoration",
            "Electricity", "Light & Sound with Vasan", "Cleaning Stuffs", "Transportation",
            "Bisarjan & Feast", "Pandal", "Management & Stationary", "Dhak",
            "Cultural Program", "Advertisment", "Flowers", "Lakshmi Puja", "New Assets",
            "Khuti Puja", "Pritivoj", "Others",
            "Catering", "Finance", "Idol & Puja", "Logistics", "Permissions & Legal", "Publicity",
        ]),
        ("ad_type", ["Banner", "Leaflet", "Banner + Leaflet", "Front Page Leaflet", "Others"]),
        ("ad_status", [
            "Write-up received", "Banner received", "Banner received, Writeup Pending",
            "Write-up received, Banner Pending", "Pending All", "NA",
        ]),
        ("offering_type", ["Vog", "Saree", "Dhuti", "Flower", "Others"]),
        ("puja_day", ["Mahalaya", "Panchami", "Sasthi", "Saptami", "Astami", "Sandhi Puja", "Nabami", "Dasami"]),
        ("donation_purpose", ["Idol", "Feast", "Vog", "General", "Others"]),
        ("vendor_category", [
            "Pandal", "Decoration", "Light & Sound", "Idol", "Catering",
            "Dhak & Music", "Printing", "Transport", "Security", "Others",
        ]),
    ];

    public async Task EnsureSeededAsync(CancellationToken ct = default)
    {
        if (await db.Organizations.AnyAsync(ct))
        {
            return;
        }

        // The fixed id is what single-committee mode resolves against
        // (SessionService.DefaultOrgId) — every row this app writes is stamped
        // with it, so a fresh deployment has to use the same one.
        var orgId = SessionService.DefaultOrgId;
        var name = configuration["Committee:Name"] ?? "Durga Puja Committee";
        var slug = configuration["Committee:Slug"] ?? Slugify(name);
        var city = configuration["Committee:City"];
        var year = int.TryParse(configuration["Committee:FirstYear"], out var configured)
            ? configured
            : DateTime.UtcNow.Year;

        logger.LogInformation("Empty database — bootstrapping committee {Name} ({Slug}) for {Year}", name, slug, year);

        db.Organizations.Add(new Organization
        {
            Id = orgId,
            Name = name,
            Slug = slug,
            City = city,
            IsActive = true,
        });

        db.Zones.AddRange(Zones.Select((zone, index) => new Zone
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            Name = zone,
            SortOrder = index + 1,
        }));

        db.Lookups.AddRange(Lookups.SelectMany(group =>
            group.Labels.Select((label, index) => new Lookup
            {
                Id = Guid.NewGuid(),
                OrgId = orgId,
                Category = group.Category,
                Label = label,
                SortOrder = index + 1,
                Active = true,
            })));

        // No collectors seeded: the names in the old schema were one specific
        // committee's volunteers, which is exactly the kind of thing a new
        // deployment should not inherit. Admins add their own under Settings.
        db.PujaYears.Add(new PujaYear
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            Year = year,
            Name = $"Durga Puja {year}",
            Target = 0,
            ProjectionPct = 6.50m,
            IsActive = true,
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Committee bootstrapped. First admin is granted on first sign-in approval.");
    }

    private static string Slugify(string value) =>
        new string(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-')
            .Replace("--", "-");
}
