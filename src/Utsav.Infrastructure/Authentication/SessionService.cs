using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Authentication;

/// <summary>
/// Ported from <c>server/auth.mjs</c>: <c>createSession</c>, <c>destroySession</c>,
/// <c>loadSession</c>. Single-committee mode — every account is a membership
/// request against this one fixed organization; see the Node comment this
/// mirrors for the full rationale.
/// </summary>
public sealed class SessionService(UtsavDbContext db)
{
    public static readonly Guid DefaultOrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task<string> CreateSessionAsync(Guid userId, CancellationToken ct = default)
    {
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        db.Sessions.Add(new Session { Token = token, UserId = userId, OrgId = DefaultOrgId });
        await db.SaveChangesAsync(ct);
        return token;
    }

    public Task DestroySessionAsync(string token, CancellationToken ct = default) =>
        db.Sessions.Where(s => s.Token == token).ExecuteDeleteAsync(ct);

    /// <summary>Null if the token doesn't resolve to a user at all (missing/expired/invalid).</summary>
    public async Task<SessionPayload?> LoadSessionAsync(string token, CancellationToken ct = default)
    {
        var userRow = await (
            from s in db.Sessions
            join u in db.Users on s.UserId equals u.Id
            where s.Token == token
            select u
        ).SingleOrDefaultAsync(ct);

        if (userRow is null)
        {
            return null;
        }

        var user = new SessionUser(userRow.Id, userRow.Email, userRow.FullName, userRow.AuthProvider);

        var membership = await db.OrgMembers
            .Where(m => m.OrgId == DefaultOrgId && m.UserId == userRow.Id)
            .Select(m => new { m.Role, m.Status })
            .SingleOrDefaultAsync(ct);

        if (membership?.Status != "active")
        {
            return new SessionPayload(user, Org: null, Role: null, Status: membership?.Status ?? "none");
        }

        var org = await db.Organizations
            .Where(o => o.Id == DefaultOrgId)
            .Select(o => new SessionOrganization(o.Id, o.Name, o.Slug))
            .SingleOrDefaultAsync(ct);

        return new SessionPayload(user, org, membership.Role, "active");
    }
}
