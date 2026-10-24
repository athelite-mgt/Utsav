using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Authentication;

/// <summary>
/// Opaque session tokens stored server-side, so revocation is a row delete
/// rather than a token-expiry wait.
///
/// A session is pinned to at most one committee at a time
/// (<c>sessions.org_id</c>). It starts unpinned when the user belongs to
/// several, and the client pins it with <see cref="SwitchOrgAsync"/>. The
/// tenant therefore always comes from this row, never from a header, query
/// string or body, which a caller could forge.
/// </summary>
public sealed class SessionService(UtsavDbContext db)
{
    public async Task<string> CreateSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        // Pin automatically when there is exactly one active membership: the
        // overwhelmingly common case, and showing a one-item chooser would be
        // a worse experience than just going in.
        var active = await db.OrgMembers
            .Where(m => m.UserId == userId && m.Status == "active")
            .Select(m => m.OrgId)
            .ToListAsync(ct);

        db.Sessions.Add(new Session
        {
            Token = token,
            UserId = userId,
            OrgId = active.Count == 1 ? active[0] : null,
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public Task DestroySessionAsync(string token, CancellationToken ct = default) =>
        db.Sessions.Where(s => s.Token == token).ExecuteDeleteAsync(ct);

    /// <summary>
    /// Pins an existing session to a different committee. Returns false,
    /// changing nothing, when the caller is not an active member there, so a
    /// forged org id cannot move a session somewhere it does not belong.
    /// </summary>
    public async Task<bool> SwitchOrgAsync(Guid userId, Guid orgId, string token, CancellationToken ct = default)
    {
        var allowed = await db.OrgMembers
            .AnyAsync(m => m.UserId == userId && m.OrgId == orgId && m.Status == "active", ct);
        if (!allowed)
        {
            return false;
        }

        var session = await db.Sessions
            .AsTracking()
            .SingleOrDefaultAsync(s => s.Token == token && s.UserId == userId, ct);
        if (session is null)
        {
            return false;
        }

        session.OrgId = orgId;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Null if the token doesn't resolve to a user at all (missing/expired/invalid).</summary>
    public async Task<SessionPayload?> LoadSessionAsync(string token, CancellationToken ct = default)
    {
        var row = await (
            from s in db.Sessions
            join u in db.Users on s.UserId equals u.Id
            where s.Token == token
            select new { s.OrgId, User = u }
        ).SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var user = new SessionUser(row.User.Id, row.User.Email, row.User.FullName, row.User.AuthProvider);

        var membershipRows = await (
            from m in db.OrgMembers
            join o in db.Organizations on m.OrgId equals o.Id
            where m.UserId == row.User.Id && o.IsActive
            orderby o.Name
            select new { o.Id, o.Name, o.Slug, o.LogoBlob, o.AccentColour, m.Role, m.Status }
        ).ToListAsync(ct);

        var memberships = membershipRows
            .Select(m => new SessionMembership(
                m.Id, m.Name, m.Slug, BrandingUrls.LogoUrl(m.Id, m.LogoBlob), m.AccentColour, m.Role, m.Status))
            .ToList();

        if (row.OrgId is null)
        {
            // Signed in, no committee chosen. "active" means the chooser has
            // options; anything else sends them to the pending screen.
            var anyActive = memberships.Any(m => m.Status == "active");
            return new SessionPayload(user, null, null, anyActive ? "active" : BestStatus(memberships), memberships, row.User.IsPlatformAdmin);
        }

        // Re-verified every request, deliberately: this is what makes a
        // revoked membership stop working immediately rather than at expiry.
        var current = memberships.SingleOrDefault(m => m.OrgId == row.OrgId.Value);
        if (current is null || current.Status != "active")
        {
            return new SessionPayload(user, null, null, current?.Status ?? BestStatus(memberships), memberships, row.User.IsPlatformAdmin);
        }

        var org = new SessionOrganization(current.OrgId, current.Name, current.Slug, current.LogoUrl, current.AccentColour);
        return new SessionPayload(user, org, current.Role, "active", memberships, row.User.IsPlatformAdmin);
    }

    private static string BestStatus(IReadOnlyList<SessionMembership> memberships) =>
        memberships.Any(m => m.Status == "pending") ? "pending" : "none";
}
