using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Ported from server/app.mjs lines 189-276. Field sets match the Node
/// handlers' "SELECT *" exactly (including org_id, which the frontend's
/// TypeScript interfaces don't declare but the wire response has always
/// included) rather than trimming to what the UI currently reads.
/// </summary>
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.ActiveMember);

        group.MapGet("/puja_years", async (UtsavDbContext db, CancellationToken ct) =>
            await db.PujaYears
                .Where(y => y.OrgId == db.TenantId)
                .OrderByDescending(y => y.Year)
                .Select(y => new
                {
                    y.Id,
                    y.OrgId,
                    y.Year,
                    y.Name,
                    y.Target,
                    y.ProjectionPct,
                    y.IsActive,
                    y.Notes,
                    y.CreatedAt,
                })
                .ToListAsync(ct));

        group.MapGet("/zones", async (UtsavDbContext db, CancellationToken ct) =>
            await db.Zones
                .Where(z => z.OrgId == db.TenantId)
                .OrderBy(z => z.SortOrder).ThenBy(z => z.Name)
                .Select(z => new { z.Id, z.OrgId, z.Name, z.SortOrder })
                .ToListAsync(ct));

        group.MapGet("/collectors", async (UtsavDbContext db, CancellationToken ct) =>
            await db.Collectors
                .Where(c => c.OrgId == db.TenantId)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new { c.Id, c.OrgId, c.Name, c.Phone, c.UserId, c.Active, c.SortOrder })
                .ToListAsync(ct));

        // No category -> every lookup for the org, grouped by category, no
        // active filter. With category -> just the active ones, alphabetised
        // — the shape each caller in the Node app actually relied on.
        group.MapGet("/lookups", async (string? category, UtsavDbContext db, CancellationToken ct) =>
        {
            var scoped = db.Lookups.Where(l => l.OrgId == db.TenantId);
            var ordered = category is null
                ? scoped.OrderBy(l => l.Category).ThenBy(l => l.SortOrder)
                : scoped.Where(l => l.Category == category && l.Active).OrderBy(l => l.SortOrder).ThenBy(l => l.Label);

            return await ordered
                .Select(l => new { l.Id, l.OrgId, l.Category, l.Label, l.SortOrder, l.Active })
                .ToListAsync(ct);
        });

        return app;
    }
}
