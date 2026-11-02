using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Endpoints;

/// <summary>
/// A committee can switch the Feast section off. Nothing is deleted: while off
/// the Feast routes simply stop answering, and turning it back on restores
/// everything exactly as it was.
/// </summary>
internal static class FeastSwitch
{
    public static readonly IReadOnlySet<string> Tables = new HashSet<string>(StringComparer.Ordinal) { "feasts", "feast_expenses" };

    public static Task<bool> IsOnAsync(UtsavDbContext db, CancellationToken ct) =>
        db.Organizations.AsNoTracking().Where(o => o.Id == db.TenantId).Select(o => o.FeastEnabled).SingleOrDefaultAsync(ct);

    public static IResult Off() =>
        Results.Json(new { error = "Feast is switched off for this committee" }, statusCode: 403);

    /// <summary>Endpoint filter for the Feast routes: 404 while the section is off, as if it did not exist.</summary>
    public static async ValueTask<object?> RequireOn(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<UtsavDbContext>();
        return await IsOnAsync(db, context.HttpContext.RequestAborted)
            ? await next(context)
            : Results.NotFound();
    }
}
