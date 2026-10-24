using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Storage;
using static Utsav.Api.Endpoints.AdvanceEndpoints;

namespace Utsav.Api.Endpoints;

/// <summary>
/// A committee's logo. The colour and name ride on the existing
/// <c>PATCH /api/organizations/current</c>; the logo needs bytes, so it has its
/// own routes.
///
/// Changing branding takes <see cref="Permission.ManageSettings"/>, there is no
/// way to name another committee (everything is "current"), and the logo is
/// stored only under that committee's own <c>{orgId}/branding/</c> prefix.
/// </summary>
public static class BrandingEndpoints
{
    /// <summary>A logo is a small image. Anything larger is a mistake or an abuse.</summary>
    private const int MaxLogoBytes = 512 * 1024;

    public static IEndpointRouteBuilder MapBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        // Public: an <img> tag cannot send the bearer token, and a committee's
        // logo is a brand asset, not a secret. Looked up by the unguessable
        // committee id.
        app.MapGet("/api/branding/{orgId:guid}/logo", LogoAsync).AllowAnonymous();

        var admin = app.MapGroup("/api/organizations/current").RequirePermission(Permission.ManageSettings);
        admin.MapPost("/logo", UploadAsync).DisableAntiforgery();
        admin.MapDelete("/logo", RemoveAsync);

        return app;
    }

    private static async Task<IResult> LogoAsync(
        Guid orgId, UtsavDbContext db, IBlobStorageService storage, HttpContext http, CancellationToken ct)
    {
        var blob = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId && o.IsActive)
            .Select(o => o.LogoBlob)
            .SingleOrDefaultAsync(ct);

        // The bytes are fetched by a stored path, so the path has to lie inside
        // this committee's own branding prefix, exactly as for documents.
        if (blob is null || !storage.IsConfigured || !blob.StartsWith(BrandingUrls.BlobPrefix(orgId), StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        // Content type comes from OUR extension for the blob, never from
        // anything the uploader claimed.
        var kind = ImageKind.ForExtension(Path.GetExtension(blob).TrimStart('.'));
        if (kind is null)
        {
            return Results.NotFound();
        }

        var download = await storage.DownloadAsync(blob, ct);

        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Even if a browser were somehow persuaded to treat it as a document,
        // it could run nothing.
        http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        // The URL carries a version tag that changes with the logo, so it can be cached hard.
        http.Response.Headers.CacheControl = "public, max-age=86400";
        return Results.File(download.Content, kind.ContentType);
    }

    private static async Task<IResult> UploadAsync(
        IFormFile file, UtsavDbContext db, IBlobStorageService storage, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!storage.IsConfigured)
        {
            return Results.Json(new { error = "File storage isn't set up on the server yet." }, statusCode: 503);
        }

        if (file.Length == 0 || file.Length > MaxLogoBytes)
        {
            return Results.Json(new { error = $"A logo must be an image of at most {MaxLogoBytes / 1024} KB." }, statusCode: 400);
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        {
            bytes = new byte[file.Length];
            await stream.ReadExactlyAsync(bytes, ct);
        }

        // From the bytes themselves: the file name and content type are only claims.
        if (ImageKind.Detect(bytes) is not { } kind)
        {
            return Results.Json(new { error = "The logo must be a PNG, JPEG, GIF or WebP image." }, statusCode: 400);
        }

        // The name is ours entirely: nothing from the upload reaches it.
        var blobName = $"{BrandingUrls.BlobPrefix(db.TenantId)}{Guid.NewGuid():N}.{kind.Extension}";
        await using (var content = new MemoryStream(bytes))
        {
            await storage.UploadAsync(content, blobName, kind.ContentType, ct);
        }

        var previous = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == db.TenantId).Select(o => o.LogoBlob).SingleAsync(ct);

        await DynamicSql.ExecuteAsync(db, "UPDATE organizations SET logo_blob=@blob WHERE id=@org", [P("@blob", blobName), Org(db)], ct);

        await RemoveBlobQuietlyAsync(storage, loggers, db.TenantId, previous, ct);
        return Results.Ok(new { logo_url = BrandingUrls.LogoUrl(db.TenantId, blobName) });
    }

    private static async Task<IResult> RemoveAsync(
        UtsavDbContext db, IBlobStorageService storage, ILoggerFactory loggers, CancellationToken ct)
    {
        var previous = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == db.TenantId).Select(o => o.LogoBlob).SingleAsync(ct);

        await DynamicSql.ExecuteAsync(db, "UPDATE organizations SET logo_blob=NULL WHERE id=@org", [Org(db)], ct);

        await RemoveBlobQuietlyAsync(storage, loggers, db.TenantId, previous, ct);
        return Results.Ok(new { ok = true });
    }

    /// <summary>
    /// The row is the source of truth; a failed cleanup leaves an orphaned file,
    /// not a broken committee. Only ever deletes inside the committee's own
    /// prefix, so a bad stored path can never delete someone else's file.
    /// </summary>
    private static async Task RemoveBlobQuietlyAsync(
        IBlobStorageService storage, ILoggerFactory loggers, Guid orgId, string? blob, CancellationToken ct)
    {
        if (blob is null || !storage.IsConfigured)
        {
            return;
        }

        if (!blob.StartsWith(BrandingUrls.BlobPrefix(orgId), StringComparison.Ordinal))
        {
            loggers.CreateLogger("BrandingEndpoints").LogWarning(
                "Left blob {Blob} alone: it is outside committee {Org}'s branding prefix", blob, orgId);
            return;
        }

        try
        {
            await storage.DeleteAsync(blob, ct);
        }
        catch (Exception ex)
        {
            loggers.CreateLogger("BrandingEndpoints").LogWarning(ex, "Could not remove old logo blob {Blob}", blob);
        }
    }
}
