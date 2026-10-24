using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Storage;
using static Utsav.Api.Endpoints.AdvanceEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Puja permission and contract-level paperwork. Metadata lives in SQL
/// (audited the same way every other write is, via <see cref="DynamicSql"/>);
/// the bytes live in blob storage behind <see cref="IBlobStorageService"/>,
/// which isn't configured yet — every route here checks
/// <c>IsConfigured</c> first and returns a clear 503 rather than a half
/// upload, same shape as <c>PushEndpoints.TestAsync</c>.
/// </summary>
public static class DocumentEndpoints
{
    /// <summary>Generous enough for a scanned permit or a signed contract PDF; small enough not to tie up a request thread for minutes on a slow upload.</summary>
    private const long MaxUploadBytes = 25 * 1024 * 1024;

    public static readonly string[] Categories = ["Puja Permission", "Contract", "Other"];

    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var member = app.MapGroup("/api/documents").RequireAuthorization(AuthorizationPolicies.ActiveMember);
        var write = app.MapGroup("/api/documents").RequirePermission(Permission.WriteFinance);

        member.MapGet("", ListAsync);
        member.MapGet("/{id:guid}/download", DownloadAsync);
        write.MapPost("", UploadAsync).DisableAntiforgery();
        write.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid? year_id, UtsavDbContext db, CancellationToken ct)
    {
        var query = db.Documents.Where(d => d.OrgId == SessionService.DefaultOrgId);
        if (year_id is { } y)
        {
            query = query.Where(d => d.YearId == y || d.YearId == null);
        }

        var rows = await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.Category,
                d.Notes,
                d.FileName,
                d.ContentType,
                d.SizeBytes,
                d.CreatedAt,
                d.YearId,
                d.ContractId,
                ContractTitle = d.Contract != null ? d.Contract.Title : null,
                UploadedByName = d.UploadedBy != null ? (d.UploadedBy.FullName ?? d.UploadedBy.Email) : null,
            })
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    private static async Task<IResult> UploadAsync(
        IFormFile file,
        [FromForm] string title,
        [FromForm] string? category,
        [FromForm] string? notes,
        [FromForm] string? year_id,
        [FromForm] string? contract_id,
        HttpContext http, UtsavDbContext db, IBlobStorageService storage, CancellationToken ct)
    {
        if (!storage.IsConfigured)
        {
            return Results.Json(new { error = "Document storage isn't set up on the server yet." }, statusCode: 503);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Results.Json(new { error = "title is required" }, statusCode: 400);
        }

        if (file.Length == 0)
        {
            return Results.Json(new { error = "A file is required" }, statusCode: 400);
        }

        if (file.Length > MaxUploadBytes)
        {
            return Results.Json(new { error = $"File is too large — {MaxUploadBytes / (1024 * 1024)} MB max." }, statusCode: 400);
        }

        var yearId = Guid.TryParse(year_id, out var y) ? y : (Guid?)null;
        var contractId = Guid.TryParse(contract_id, out var c) ? c : (Guid?)null;
        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;

        // Org-prefixed and randomised so two committees (or two uploads of a
        // same-named file) never collide in the one shared container.
        var blobName = $"{SessionService.DefaultOrgId}/{Guid.NewGuid()}-{Path.GetFileName(file.FileName)}";

        await using (var stream = file.OpenReadStream())
        {
            await storage.UploadAsync(stream, blobName, file.ContentType, ct);
        }

        var id = Guid.NewGuid();
        await DynamicSql.ExecuteAsync(db, """
            INSERT INTO documents (id, org_id, year_id, contract_id, title, category, notes, file_name, blob_name, content_type, size_bytes, uploaded_by_user_id)
            VALUES (@id, @org, @year, @contract, @title, @category, @notes, @file_name, @blob_name, @content_type, @size, @uploaded_by)
            """,
            [
                P("@id", id), Org(), P("@year", (object?)yearId ?? DBNull.Value), P("@contract", (object?)contractId ?? DBNull.Value),
                P("@title", title.Trim()), P("@category", (object?)category ?? DBNull.Value), P("@notes", (object?)notes ?? DBNull.Value),
                P("@file_name", file.FileName), P("@blob_name", blobName), P("@content_type", file.ContentType),
                P("@size", file.Length), P("@uploaded_by", session.User.Id),
            ], ct);

        var saved = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == id, ct);
        return Results.Ok(new
        {
            saved.Id, saved.Title, saved.Category, saved.Notes, saved.FileName, saved.ContentType,
            saved.SizeBytes, saved.CreatedAt, saved.YearId, saved.ContractId,
        });
    }

    private static async Task<IResult> DownloadAsync(Guid id, UtsavDbContext db, IBlobStorageService storage, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id && d.OrgId == SessionService.DefaultOrgId, ct);
        if (doc is null)
        {
            return Results.NotFound();
        }

        if (!storage.IsConfigured)
        {
            return Results.Json(new { error = "Document storage isn't set up on the server yet." }, statusCode: 503);
        }

        var download = await storage.DownloadAsync(doc.BlobName, ct);
        return Results.File(download.Content, download.ContentType ?? doc.ContentType, doc.FileName);
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, UtsavDbContext db, IBlobStorageService storage, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id && d.OrgId == SessionService.DefaultOrgId, ct);
        if (doc is null)
        {
            return Results.NotFound();
        }

        await DynamicSql.ExecuteAsync(db, "DELETE FROM documents WHERE id=@id AND org_id=@org", [P("@id", id), Org()], ct);

        // The row is the source of truth; a failed blob cleanup afterwards
        // leaves an orphaned file, not a broken app state, so it's logged and
        // swallowed rather than surfaced as a failed delete.
        try
        {
            if (storage.IsConfigured)
            {
                await storage.DeleteAsync(doc.BlobName, ct);
            }
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("DocumentEndpoints").LogWarning(ex, "Deleted document {Id} but failed to remove its blob {BlobName}", id, doc.BlobName);
        }

        return Results.Ok(new { ok = true });
    }
}
