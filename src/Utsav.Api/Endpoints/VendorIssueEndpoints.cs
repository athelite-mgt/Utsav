using System.Text.Json;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Data;
using static Utsav.Api.Endpoints.AdvanceEndpoints;
using static Utsav.Api.Endpoints.ModuleEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// The problem log behind a vendor and, optionally, one of their contracts —
/// late delivery, quality dispute, payment disagreement, whatever a
/// committee needs on record for next season's renewal decision. Always
/// tied to a vendor; <c>contract_id</c> is only set when the issue happened
/// under a specific engagement rather than the vendor relationship broadly.
/// Reads are open to every active member (same as vendors/contracts); writes
/// need <see cref="Permission.WriteFinance"/>.
/// </summary>
public static class VendorIssueEndpoints
{
    private static readonly string[] Columns =
        ["vendor_id", "contract_id", "issue_type", "description", "occurred_on", "status"];

    public static IEndpointRouteBuilder MapVendorIssueEndpoints(this IEndpointRouteBuilder app)
    {
        var member = app.MapGroup("/api").RequireAuthorization(AuthorizationPolicies.ActiveMember);
        var write = app.MapGroup("/api").RequirePermission(Permission.WriteFinance);

        // vendor_id/contract_id narrow the log to one vendor or one contract;
        // omitting both returns every issue on record (the Vendors page's own
        // "Issues" tab wants that full list to filter client-side).
        member.MapGet("/vendor_issues", (Guid? vendor_id, Guid? contract_id, UtsavDbContext db, CancellationToken ct) =>
            QueryAsync(db, $"""
                SELECT vi.*, v.name AS vendor_name, c.title AS contract_title
                  FROM vendor_issues vi
                  JOIN vendors v ON v.id = vi.vendor_id
                  LEFT JOIN contracts c ON c.id = vi.contract_id
                 WHERE vi.org_id = @org
                   {(vendor_id is null ? "" : "AND vi.vendor_id = @vendor")}
                   {(contract_id is null ? "" : "AND vi.contract_id = @contract")}
                 ORDER BY COALESCE(vi.occurred_on, '') DESC, vi.created_at DESC
                """,
                vendor_id is null && contract_id is null
                    ? [Org()]
                    : vendor_id is null
                        ? [Org(), P("@contract", contract_id!.Value)]
                        : contract_id is null
                            ? [Org(), P("@vendor", vendor_id.Value)]
                            : [Org(), P("@vendor", vendor_id.Value), P("@contract", contract_id.Value)],
                ct));

        write.MapPost("/vendor_issues", CreateAsync);
        write.MapPatch("/vendor_issues/{id:guid}", UpdateAsync);
        write.MapDelete("/vendor_issues/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> CreateAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!body.TryGetProperty("vendor_id", out var vendorId)
            || !TryString(body, "issue_type", out var issueType)
            || !TryString(body, "description", out var description))
        {
            return Results.Json(new { error = "vendor_id, issue_type and description are required" }, statusCode: 400);
        }

        var id = Guid.NewGuid();
        await DynamicSql.ExecuteAsync(db, """
            INSERT INTO vendor_issues (id, org_id, vendor_id, contract_id, issue_type, description, occurred_on, status)
            VALUES (@id, @org, @vendor, @contract, @type, @description, @occurred, @status)
            """,
            [
                P("@id", id), Org(), P("@vendor", vendorId.GetGuid()),
                P("@contract", Opt(body, "contract_id")),
                P("@type", issueType), P("@description", description),
                P("@occurred", Opt(body, "occurred_on")),
                P("@status", TryString(body, "status", out var status) ? status : "open"),
            ], ct);

        return Results.Ok((await OneAsync(db, id, ct)).FirstOrDefault());
    }

    private static async Task<IResult> UpdateAsync(Guid id, JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        var supplied = body.ValueKind == JsonValueKind.Object
            ? body.EnumerateObject().Where(p => Columns.Contains(p.Name)).ToList()
            : [];

        if (supplied.Count == 0)
        {
            return Results.Json(new { error = "Nothing to update" }, statusCode: 400);
        }

        var assignments = string.Join(", ", supplied.Select((p, i) => $"[{p.Name}] = @p{i}"));
        var parameters = supplied.Select((p, i) => P($"@p{i}", SqlValue.FromJson(p.Value)))
            .Append(P("@id", id)).Append(Org()).ToArray();

        await DynamicSql.ExecuteAsync(db, $"UPDATE [vendor_issues] SET {assignments} WHERE id = @id AND org_id = @org;", parameters, ct);

        var row = (await OneAsync(db, id, ct)).FirstOrDefault();
        return row is null
            ? Results.Json(new { error = "Not found in this organization" }, statusCode: 404)
            : Results.Ok(row);
    }

    private static async Task<IResult> DeleteAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        await DynamicSql.ExecuteAsync(db, "DELETE FROM vendor_issues WHERE id=@id AND org_id=@org", [P("@id", id), Org()], ct);
        return Results.Ok(new { ok = true });
    }

    private static Task<List<Dictionary<string, object?>>> OneAsync(UtsavDbContext db, Guid id, CancellationToken ct) =>
        QueryAsync(db, """
            SELECT vi.*, v.name AS vendor_name, c.title AS contract_title
              FROM vendor_issues vi
              JOIN vendors v ON v.id = vi.vendor_id
              LEFT JOIN contracts c ON c.id = vi.contract_id
             WHERE vi.id = @id AND vi.org_id = @org
            """, [P("@id", id), Org()], ct);
}
