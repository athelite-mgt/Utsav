using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Issue and revoke the machine credentials an external agent uses to pull
/// the report export. Admin-only: a key is effectively a standing grant of
/// read access to the committee's whole financial position, so handing one
/// out is a committee-configuration decision, not a finance one.
///
/// There is deliberately no "show me the key again" route — see
/// <see cref="ApiKeyService"/>; only a hash is stored, so a lost key is
/// revoked and reissued rather than recovered.
/// </summary>
public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/api_keys")
            .RequirePermission(Permission.ManageSettings);

        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapDelete("/{id:guid}", RevokeAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(UtsavDbContext db, CancellationToken ct)
    {
        var rows = await db.ApiKeys.AsNoTracking()
            .Where(k => k.OrgId == SessionService.DefaultOrgId)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new
            {
                id = k.Id,
                name = k.Name,
                prefix = k.Prefix,
                created_at = k.CreatedAt,
                last_used_at = k.LastUsedAt,
                revoked_at = k.RevokedAt,
                created_by_name = k.CreatedByUser == null ? null : k.CreatedByUser.FullName ?? k.CreatedByUser.Email,
            })
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateAsync(
        CreateApiKeyRequest body, UtsavDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var name = body.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest(new { error = "A name is required" });

        var key = ApiKeyService.GenerateKey();
        var record = new ApiKey
        {
            Id = Guid.NewGuid(),
            OrgId = SessionService.DefaultOrgId,
            Name = name,
            KeyHash = ApiKeyService.HashKey(key),
            Prefix = ApiKeyService.DisplayPrefixOf(key),
            CreatedByUserId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : null,
            CreatedAt = DateTime.UtcNow,
        };

        db.ApiKeys.Add(record);
        await db.SaveChangesAsync(ct);

        // The only time the plaintext exists outside the caller's request —
        // the response says so, because there is no way to show it again.
        return Results.Ok(new
        {
            id = record.Id,
            name = record.Name,
            key,
            note = "Copy this key now — it cannot be shown again.",
        });
    }

    private static async Task<IResult> RevokeAsync(Guid id, UtsavDbContext db, CancellationToken ct)
    {
        // Soft-revoke rather than delete: the row keeps the key's name, so an
        // audit trail entry or a support question about "what was calling us"
        // still resolves to something readable.
        var affected = await db.ApiKeys
            .Where(k => k.Id == id && k.OrgId == SessionService.DefaultOrgId && k.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.RevokedAt, DateTime.UtcNow), ct);

        return affected == 0 ? Results.NotFound() : Results.NoContent();
    }

    public sealed record CreateApiKeyRequest(string? Name);
}
