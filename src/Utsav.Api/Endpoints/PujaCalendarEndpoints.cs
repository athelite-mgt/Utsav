using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// The festival day→date map for a puja year (Mahalaya … Dasami). Reads here;
/// writes go through the generic CRUD route (see TableRegistry) so admins /
/// secretariat can adjust dates to their panjika.
/// </summary>
public static class PujaCalendarEndpoints
{
    public static IEndpointRouteBuilder MapPujaCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/puja_calendar_days", ListAsync)
           .RequireAuthorization(AuthorizationPolicies.ActiveMember);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid year_id, UtsavDbContext db, CancellationToken ct)
    {
        var rows = await db.PujaCalendarDays.AsNoTracking()
            .Where(d => d.OrgId == db.TenantId && d.YearId == year_id)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.EventDate)
            .Select(d => new
            {
                id = d.Id,
                year_id = d.YearId,
                label = d.Label,
                event_date = d.EventDate,
                note = d.Note,
                sort_order = d.SortOrder,
            })
            .ToListAsync(ct);

        return Results.Ok(rows);
    }
}
