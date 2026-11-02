using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Authentication;

/// <summary>
/// Opaque session tokens stored server-side, so revocation is a row delete
/// rather than a token-expiry wait.
///
/// The browser holds the raw token; only its hash is stored (see
/// <see cref="SessionTokens"/>), so every method here takes the RAW token and
/// hashes it before touching the database.
///
/// A session ends at an absolute deadline however active it has been, and
/// sooner if it sits idle; both are enforced here, on every load, and an
/// expired row is deleted rather than merely refused.
///
/// A session is pinned to at most one committee at a time
/// (<c>sessions.org_id</c>). It starts unpinned when the user belongs to
/// several, and the client pins it with <see cref="SwitchOrgAsync"/>. The
/// tenant therefore always comes from this row, never from a header, query
/// string or body, which a caller could forge.
/// </summary>
public sealed class SessionService(UtsavDbContext db, TimeProvider? clock = null)
{
    /// <summary>However active it has been, a session ends this long after sign-in.</summary>
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(30);

    /// <summary>A session unused this long ends, so a forgotten browser does not stay signed in for a month.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromDays(14);

    /// <summary>Activity is recorded at most this often: a write on every request would be pure load.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<string> CreateSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var token = SessionTokens.Generate();
        var now = Now;

        // Pin automatically when there is exactly one active membership: the
        // overwhelmingly common case, and showing a one-item chooser would be
        // a worse experience than just going in.
        var active = await db.OrgMembers
            .Where(m => m.UserId == userId && m.Status == "active")
            .Select(m => m.OrgId)
            .ToListAsync(ct);

        db.Sessions.Add(new Session
        {
            Token = SessionTokens.Hash(token),
            UserId = userId,
            OrgId = active.Count == 1 ? active[0] : null,
            ExpiresAt = now + AbsoluteLifetime,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public async Task DestroySessionAsync(string token, CancellationToken ct = default)
    {
        var hash = SessionTokens.Hash(token);
        var rows = await db.Sessions.AsTracking().Where(s => s.Token == hash).ToListAsync(ct);
        db.Sessions.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

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

        var hash = SessionTokens.Hash(token);
        var session = await db.Sessions
            .AsTracking()
            .SingleOrDefaultAsync(s => s.Token == hash && s.UserId == userId, ct);
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
        var hash = SessionTokens.Hash(token);
        var row = await (
            from s in db.Sessions
            join u in db.Users on s.UserId equals u.Id
            where s.Token == hash
            select new { s.OrgId, s.ExpiresAt, s.LastSeenAt, User = u }
        ).SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var now = Now;
        if (now >= row.ExpiresAt || now - row.LastSeenAt >= IdleTimeout)
        {
            // Deleted, not just refused: a dead row is dead weight, and leaving
            // it invites a later bug from treating it as live.
            await DestroySessionAsync(token, ct);
            return null;
        }

        if (now - row.LastSeenAt >= TouchInterval)
        {
            var live = await db.Sessions.AsTracking().SingleOrDefaultAsync(s => s.Token == hash, ct);
            if (live is not null)
            {
                live.LastSeenAt = now;
                await db.SaveChangesAsync(ct);
            }
        }

        var user = new SessionUser(row.User.Id, row.User.Email, row.User.FullName, row.User.AuthProvider);

        var membershipRows = await (
            from m in db.OrgMembers
            join o in db.Organizations on m.OrgId equals o.Id
            where m.UserId == row.User.Id && o.IsActive
            orderby o.Name
            select new { o.Id, o.Name, o.Slug, o.City, o.LogoBlob, o.AccentColour, m.Role, m.Status }
        ).ToListAsync(ct);

        var memberships = membershipRows
            .Select(m => new SessionMembership(
                m.Id, m.Name, m.Slug, m.City, BrandingUrls.LogoUrl(m.Id, m.LogoBlob), m.AccentColour, m.Role, m.Status))
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

        var org = new SessionOrganization(current.OrgId, current.Name, current.Slug, current.City, current.LogoUrl, current.AccentColour);
        return new SessionPayload(user, org, current.Role, "active", memberships, row.User.IsPlatformAdmin);
    }

    private static string BestStatus(IReadOnlyList<SessionMembership> memberships) =>
        memberships.Any(m => m.Status == "pending") ? "pending" : "none";
}
