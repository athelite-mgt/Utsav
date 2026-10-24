using System.Text;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Audit;
using Utsav.Infrastructure.Authentication;
using Permission = Utsav.Domain.Authorization.Permission;

namespace Utsav.Api.Endpoints;

/// <summary>
/// TASKLIST §5.7 — read side of the audit trail <see cref="DynamicSql"/>
/// writes automatically. Admin + Secretariat only, per the permission matrix.
/// Queries Cosmos directly rather than through EF/SQL Server — the audit
/// trail doesn't live in Azure SQL.
///
/// Returns a display-ready shape rather than the raw <see cref="AuditEntry"/>:
/// the stored entry is a forensic record (raw column names, before/after dicts),
/// but History wants "who / when / page / record / field: old → new". Building
/// that here keeps the page dumb and, crucially, controls the wire property
/// names explicitly instead of leaving the raw record to the global snake_case
/// policy (which is what silently turned every "who" into "System").
/// </summary>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api")
            .RequirePermission(Permission.ReadAudit)
            .MapGet("/audit", ListAsync);

        return app;
    }

    // Columns that carry no meaning to a committee member reading History — the
    // row's own plumbing, or foreign keys that are just GUIDs on the wire.
    private static readonly HashSet<string> HiddenFields = new(StringComparer.Ordinal)
    {
        // "org"/"id" are the ambient WHERE-clause parameters CrudEndpoints adds
        // to every write (SqlParameter("@org", …), SqlParameter("@id", …)) —
        // pure plumbing, never a column a person edited. "org_id"/"id" cover
        // the same columns when they show up under their real name instead
        // (e.g. in a hand-written endpoint's before-image).
        // zone_id/collector_id/vendor_id are deliberately NOT here — a
        // committee member changing a donor's zone or a booking's collector
        // is a real, expected change, and DynamicSql.ResolveFriendlyFkValuesAsync
        // already turns the raw GUID into a readable name before this ever
        // gets stored. Hiding them made a zone reassignment invisible in
        // History even though it was captured correctly underneath.
        "org", "org_id", "year_id", "id", "created_at", "updated_at", "history",
        "donor_id", "advertiser_id", "contract_id", "advance_id", "user_id", "sort_order",
        // collection_payments plumbing — parent_id is a GUID the entity label
        // already names, parent_type is redundant with the Page column, and
        // recorded_by_user_id is a GUID (recorded_by_name carries the name).
        "parent_id", "parent_type", "recorded_by_user_id",
    };

    private static async Task<IResult> ListAsync(
        CosmosClient cosmos, IConfiguration configuration,
        string? entityType, Guid? userId, DateTimeOffset? from, DateTimeOffset? to,
        int? limit, string? continuationToken, CancellationToken ct)
    {
        var database = configuration["Cosmos:Database"]
            ?? throw new InvalidOperationException("Missing configuration: Cosmos:Database");
        var containerName = configuration["Cosmos:Container"]
            ?? throw new InvalidOperationException("Missing configuration: Cosmos:Container");
        var container = cosmos.GetContainer(database, containerName);

        var orgId = SessionService.DefaultOrgId.ToString();
        var sql = new StringBuilder("SELECT * FROM c WHERE c.orgId = @org");
        var parameters = new List<(string Name, object Value)> { ("@org", orgId) };

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            sql.Append(" AND c.entityType = @entityType");
            parameters.Add(("@entityType", entityType));
        }

        if (userId is { } uid)
        {
            sql.Append(" AND c.userId = @userId");
            parameters.Add(("@userId", uid.ToString()));
        }

        // Formatted the same way System.Text.Json serialises the stored
        // DateTimeOffset (fixed-width "O", always a +00:00 offset since
        // every entry is stamped in UTC) so the lexicographic comparison
        // Cosmos does on a string property lines up with real chronological
        // order.
        if (from is { } f)
        {
            sql.Append(" AND c.timestamp >= @from");
            parameters.Add(("@from", f.ToUniversalTime().ToString("O")));
        }

        if (to is { } t)
        {
            sql.Append(" AND c.timestamp <= @to");
            parameters.Add(("@to", t.ToUniversalTime().ToString("O")));
        }

        sql.Append(" ORDER BY c.timestamp DESC");

        var query = new QueryDefinition(sql.ToString());
        foreach (var (name, value) in parameters)
        {
            query = query.WithParameter(name, value);
        }

        var pageSize = Math.Clamp(limit ?? 50, 1, 200);
        using var iterator = container.GetItemQueryIterator<AuditEntry>(
            query,
            continuationToken,
            new QueryRequestOptions { MaxItemCount = pageSize, PartitionKey = new PartitionKey(orgId) });

        var page = await iterator.ReadNextAsync(ct);
        var items = page.Select(ToView).ToList();

        // Explicit snake_case names on the anonymous object match the frontend
        // AuditRow type; the global JSON policy then leaves them alone.
        return Results.Ok(new { items, continuation_token = page.ContinuationToken });
    }

    /// <summary>Public (not anonymous) so <see cref="ToView"/>/<see cref="BuildChanges"/> — the exact mapping that used to silently turn every "who" into "System" — are directly unit-testable.</summary>
    internal sealed record AuditRowView(
        string Id, DateTimeOffset Timestamp, string Who, string? Role, string Page, string Action,
        string? Record, string Outcome, string? ErrorMessage, string? RequestMethod, string? RequestPath,
        string? IpAddress, List<AuditChangeView> Changes);

    internal sealed record AuditChangeView(string Field, object? From, object? To);

    internal static AuditRowView ToView(AuditEntry e) => new(
        Id: e.Id,
        Timestamp: e.Timestamp,
        Who: e.UserFullName ?? e.UserEmail ?? "System",
        Role: e.UserRole,
        Page: AuditableTables.PageFor(e.EntityType),
        // "Created" / "Updated" / "Deleted" — the word a reader expects, not the SQL verb.
        Action: e.Action switch
        {
            "INSERT INTO" => "Created",
            "UPDATE" => "Updated",
            "DELETE" => "Deleted",
            _ => e.Action,
        },
        Record: e.EntityLabel,
        Outcome: e.Outcome,
        ErrorMessage: e.ErrorMessage,
        RequestMethod: e.RequestMethod,
        RequestPath: e.RequestPath,
        IpAddress: e.IpAddress,
        // Field-by-field values for every action: an update's old→new, a
        // create's initial values (before is absent, so every field reads
        // null → value), a delete's final values (after is absent, so every
        // field reads value → null — a reader sees exactly what was removed).
        Changes: BuildChanges(e));

    internal static List<AuditChangeView> BuildChanges(AuditEntry e)
    {
        var before = e.Before is { Count: > 0 } ? e.Before[0] : null;
        var after = e.After ?? new Dictionary<string, object?>();
        if (before is null && after.Count == 0)
        {
            return [];
        }

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in after.Keys)
        {
            keys.Add(k);
        }

        if (before is not null)
        {
            foreach (var k in before.Keys)
            {
                keys.Add(k);
            }
        }

        var changes = new List<AuditChangeView>();
        foreach (var key in keys)
        {
            if (HiddenFields.Contains(key))
            {
                continue;
            }

            var fromValue = before is not null && before.TryGetValue(key, out var bv) ? bv : null;
            var toValue = after.TryGetValue(key, out var av) ? av : null;

            changes.Add(new AuditChangeView(key, fromValue, toValue));
        }

        return changes;
    }
}
