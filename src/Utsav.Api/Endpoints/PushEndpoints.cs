using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using Utsav.Infrastructure.Push;
using static Utsav.Api.Endpoints.AdvanceEndpoints;
using AuditEntry = Utsav.Infrastructure.Audit.AuditEntry;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Registering, unregistering and test-firing Web Push on this device.
/// Open to any active member regardless of role (same as the "Install on
/// your phone" row it sits beside in Settings) — a Viewer opting in today
/// costs nothing and means nothing has to change if their role is upgraded
/// later. Whether a push actually gets <em>sent</em> to a given member is
/// decided separately, per notification, in <c>DynamicSql.TryNotifyAsync</c>.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var member = app.MapGroup("/api/push").RequireAuthorization(AuthorizationPolicies.ActiveMember);
        var admin = app.MapGroup("/api/push").RequireAuthorization(AuthorizationPolicies.Admin);

        member.MapGet("/vapid-public-key", (IOptions<VapidOptions> vapid) =>
            Results.Ok(new { publicKey = vapid.Value.IsConfigured ? vapid.Value.PublicKey : null }));

        member.MapPost("/subscribe", SubscribeAsync);
        member.MapPost("/unsubscribe", UnsubscribeAsync);
        member.MapPost("/test", TestAsync);
        admin.MapPost("/announce", AnnounceAsync);

        // The in-app history behind the Notifications page. Open to any active
        // member: it's a log of the same data everyone can already read on the
        // module pages — the role restriction on push is about not buzzing
        // phones, not about hiding what changed.
        var notifications = app.MapGroup("/api/notifications")
            .RequireAuthorization(AuthorizationPolicies.ActiveMember);
        notifications.MapGet("", ListNotificationsAsync);
        // On-demand detail for one notification — who/when/page/record and the
        // field-level diff, correlated to the audit entry that caused it rather
        // than stored a second time on the notification row. See GetDetailAsync.
        notifications.MapGet("/{id:guid}/detail", GetDetailAsync);

        return app;
    }

    private static async Task<IResult> SubscribeAsync(JsonElement body, HttpContext http, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "endpoint", out var endpoint))
        {
            return Results.Json(new { error = "endpoint is required" }, statusCode: 400);
        }

        if (!body.TryGetProperty("keys", out var keys) || keys.ValueKind != JsonValueKind.Object
            || !TryString(keys, "p256dh", out var p256dh) || !TryString(keys, "auth", out var auth))
        {
            return Results.Json(new { error = "keys.p256dh and keys.auth are required" }, statusCode: 400);
        }

        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;
        var userAgent = http.Request.Headers.UserAgent.ToString();

        // A device re-subscribing — possibly signed in as someone else now —
        // replaces its old row rather than accumulating duplicate endpoints,
        // which would each get their own copy of every push.
        await DynamicSql.ExecuteAsync(db, "DELETE FROM push_subscriptions WHERE endpoint=@endpoint AND org_id=@org", [P("@endpoint", endpoint)], ct);
        await DynamicSql.ExecuteAsync(db, """
            INSERT INTO push_subscriptions (id, org_id, user_id, endpoint, p256dh, auth, user_agent)
            VALUES (@id, @org, @user, @endpoint, @p256dh, @auth, @ua)
            """,
            [
                P("@id", Guid.NewGuid()), Org(db), P("@user", session.User.Id), P("@endpoint", endpoint),
                P("@p256dh", p256dh), P("@auth", auth), P("@ua", string.IsNullOrEmpty(userAgent) ? DBNull.Value : userAgent),
            ], ct);

        return Results.Ok(new { ok = true });
    }

    private static async Task<IResult> UnsubscribeAsync(JsonElement body, UtsavDbContext db, CancellationToken ct)
    {
        if (!TryString(body, "endpoint", out var endpoint))
        {
            return Results.Json(new { error = "endpoint is required" }, statusCode: 400);
        }

        await DynamicSql.ExecuteAsync(db, "DELETE FROM push_subscriptions WHERE endpoint=@endpoint AND org_id=@org",
            [P("@endpoint", endpoint), Org(db)], ct);
        return Results.Ok(new { ok = true });
    }

    /// <summary>
    /// Sends straight through <see cref="WebPushSender"/> rather than
    /// queueing through <see cref="IPushDispatcher"/> — this is the one push
    /// a person is sitting there waiting to see, to confirm their own device
    /// works, not a fan-out to the rest of the committee.
    /// </summary>
    private static async Task<IResult> TestAsync(HttpContext http, UtsavDbContext db, WebPushSender sender, CancellationToken ct)
    {
        if (!sender.IsConfigured)
        {
            return Results.Json(new { error = "Push notifications aren't set up on the server yet." }, statusCode: 503);
        }

        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;
        var subs = await db.PushSubscriptionRecords.Where(s => s.UserId == session.User.Id).ToListAsync(ct);
        if (subs.Count == 0)
        {
            return Results.Json(new { error = "No devices registered on this account yet." }, statusCode: 400);
        }

        var payload = JsonSerializer.Serialize(new
        {
            title = "Puja Finance",
            body = "Test notification — if you can see this, it works.",
            url = "/",
        });

        var kept = new List<PushSubscriptionRecord>();
        var gone = new List<PushSubscriptionRecord>();
        foreach (var sub in subs)
        {
            if (await sender.SendAsync(sub, payload, ct))
            {
                kept.Add(sub);
            }
            else
            {
                gone.Add(sub);
            }
        }

        if (gone.Count > 0)
        {
            db.PushSubscriptionRecords.RemoveRange(gone);
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new { sent = kept.Count, removed = gone.Count });
    }

    /// <summary>
    /// The committee-wide broadcast: Admin only, every active member
    /// including Viewer (see <see cref="PushNotificationRequest.AllMembers"/>),
    /// queued through the same channel a transaction update uses rather than
    /// sent inline — one slow/failed device shouldn't hold up the request.
    /// </summary>
    private static async Task<IResult> AnnounceAsync(
        JsonElement body, HttpContext http, IPushDispatcher dispatcher, Utsav.Domain.Tenancy.ITenantContext tenant, CancellationToken ct)
    {
        if (!TryString(body, "title", out var title) || !TryString(body, "body", out var messageBody))
        {
            return Results.Json(new { error = "title and body are required" }, statusCode: 400);
        }

        var url = TryString(body, "url", out var u) && !string.IsNullOrWhiteSpace(u) ? u : "/";
        var session = (SessionPayload)http.Items[nameof(SessionPayload)]!;
        var actorName = session.User.FullName ?? session.User.Email;

        // ActorUserId null on purpose — an announcement is a deliberate
        // broadcast to everyone, so the admin who sends it gets it too (both a
        // useful delivery confirmation and genuinely "for all members"). Unlike
        // a transaction update, where you don't need to be told about your own edit.
        await dispatcher.EnqueueAsync(
            new PushNotificationRequest(tenant.TenantId, ActorUserId: null, title, messageBody, url,
                AllMembers: true, ActorName: actorName, Kind: "announcement"), ct);

        return Results.Ok(new { ok = true });
    }

    /// <summary>
    /// What changed, who changed it and what value moved to what, for one
    /// notification — found by correlating it to the audit entry it came from
    /// rather than storing the detail twice. A notification is queued (not
    /// written synchronously), so it's logged a beat after the write that
    /// triggered it; the correlation window looks slightly earlier than the
    /// notification's own timestamp to cover that gap. Same table, same actor,
    /// close in time is specific enough in practice — one person rarely edits
    /// the same module twice within moments of each other. Returns
    /// <c>{ found: false }</c> (not an error) for an announcement, a
    /// notification with no recorded actor, or when nothing in the trail
    /// matches — the page just shows the notification's own title/body then.
    /// </summary>
    private static async Task<IResult> GetDetailAsync(
        Guid id, CosmosClient cosmos, IConfiguration configuration, UtsavDbContext db, CancellationToken ct)
    {
        var notification = await db.Notifications.AsNoTracking()
            .Where(n => n.Id == id && n.OrgId == db.TenantId)
            .Select(n => new { n.Url, n.Kind, n.ActorUserId, n.CreatedAt })
            .FirstOrDefaultAsync(ct);

        if (notification is null)
        {
            return Results.NotFound(new { error = "Notification not found" });
        }

        if (notification.Kind != "transaction"
            || notification.ActorUserId is not { } actorUserId
            || !NotifiableTables.TableForRoute.TryGetValue(notification.Url, out var table))
        {
            return Results.Ok(new { found = false });
        }

        var database = configuration["Cosmos:Database"]
            ?? throw new InvalidOperationException("Missing configuration: Cosmos:Database");
        var containerName = configuration["Cosmos:Container"]
            ?? throw new InvalidOperationException("Missing configuration: Cosmos:Container");
        var container = cosmos.GetContainer(database, containerName);
        var orgId = db.TenantId.ToString();

        // Stored as Kind=Unspecified by EF (see ListNotificationsAsync's own
        // comment on the same quirk) — stamp UTC before using it in the query.
        var createdAt = DateTime.SpecifyKind(notification.CreatedAt, DateTimeKind.Utc);
        var from = createdAt.AddMinutes(-2);
        var to = createdAt.AddMinutes(1);

        var query = new QueryDefinition("""
            SELECT * FROM c
             WHERE c.orgId = @org AND c.entityType = @entityType AND c.userId = @userId
               AND c.timestamp >= @from AND c.timestamp <= @to
             ORDER BY c.timestamp DESC
            """)
            .WithParameter("@org", orgId)
            .WithParameter("@entityType", table)
            .WithParameter("@userId", actorUserId.ToString())
            .WithParameter("@from", from.ToString("O"))
            .WithParameter("@to", to.ToString("O"));

        using var iterator = container.GetItemQueryIterator<AuditEntry>(
            query, requestOptions: new QueryRequestOptions { MaxItemCount = 1, PartitionKey = new PartitionKey(orgId) });

        if (!iterator.HasMoreResults)
        {
            return Results.Ok(new { found = false });
        }

        var page = await iterator.ReadNextAsync(ct);
        var entry = page.FirstOrDefault();
        return entry is null
            ? Results.Ok(new { found = false })
            : Results.Ok(new { found = true, entry = AuditEndpoints.ToView(entry) });
    }

    /// <summary>
    /// The org's recent notification history for the Notifications page —
    /// newest first, kept to the last 24 hours and at most 5 rows (whichever
    /// is smaller), so the page always reads as "what's fresh" rather than
    /// growing into a full log. Also opportunistically purges anything past
    /// 24 hours so the table itself never accumulates — best-effort, a purge
    /// failure never blocks the read it's piggybacking on.
    /// </summary>
    private static async Task<IResult> ListNotificationsAsync(UtsavDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM notifications WHERE org_id = {db.TenantId} AND created_at < {cutoff}", ct);
        }
        catch
        {
            // Housekeeping only — never why a real request fails.
        }

        var raw = await db.Notifications.AsNoTracking()
            .Where(n => n.OrgId == db.TenantId && n.CreatedAt >= cutoff)
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .Select(n => new { n.Id, n.Title, n.Body, n.Url, n.Kind, n.ActorName, n.CreatedAt })
            .ToListAsync(ct);

        // CreatedAt comes back from SQL Server as Kind=Unspecified, which
        // System.Text.Json serialises WITHOUT a "Z"/offset — the browser then
        // parses it as local time instead of UTC, showing times hours off.
        // History's timestamps (DateTimeOffset, always carries an offset) never
        // had this problem; notifications, stored as plain DateTime, did.
        var rows = raw.Select(n => new
        {
            id = n.Id,
            title = n.Title,
            body = n.Body,
            url = n.Url,
            kind = n.Kind,
            actor_name = n.ActorName,
            created_at = DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
        });

        return Results.Ok(rows);
    }
}
