using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Utsav.Infrastructure.Data;

namespace Utsav.Infrastructure.Push;

/// <summary>
/// Drains <see cref="PushChannel"/> for the life of the app. For each queued
/// request: work out who should actually see it (every active,
/// non-viewer member of the org except whoever made the change), and send
/// one push per device they have registered.
///
/// Needs a <see cref="UtsavDbContext"/> per request it handles, so unlike
/// <see cref="Utsav.Infrastructure.Audit.AuditQueueProcessor"/> (which only
/// talks to Cosmos) this takes an <see cref="IServiceScopeFactory"/> and
/// opens a fresh scope per dequeued item — the standard shape for a
/// singleton background service that needs a scoped dependency.
/// </summary>
public sealed class PushQueueProcessor(
    PushChannel channel,
    IServiceScopeFactory scopeFactory,
    WebPushSender sender,
    ILogger<PushQueueProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!sender.IsConfigured)
        {
            logger.LogInformation("Push notifications not configured (Push:VapidPublicKey/VapidPrivateKey missing) — the Notifications page is still logged, but nothing will push to devices.");
        }

        // Every dequeued item is handled, configured or not: HandleAsync always
        // writes the in-app Notifications row, then only pushes to devices when
        // a sender is configured. So the page is a complete history even before
        // VAPID keys are set.
        await foreach (var request in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await HandleAsync(request, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to process push notification {Title}", request.Title);
            }
        }
    }

    private async Task HandleAsync(PushNotificationRequest request, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UtsavDbContext>();

        // Durable log first, so the Notifications page shows it regardless of
        // whether any device is subscribed or push is even configured.
        db.Notifications.Add(new Data.Entities.NotificationRecord
        {
            Id = Guid.NewGuid(),
            OrgId = request.OrgId,
            Title = request.Title,
            Body = request.Body,
            Url = request.Url,
            Kind = request.Kind,
            ActorUserId = request.ActorUserId,
            ActorName = request.ActorName,
        });
        await db.SaveChangesAsync(ct);

        if (!sender.IsConfigured)
        {
            return;
        }

        // Who hears about it, minus whoever triggered it:
        //  • a transaction update goes to the money-responsible roles only —
        //    Admin, Secretariat, Finance (not Executive, not Viewer);
        //  • an admin announcement goes to every active member — see
        //    PushNotificationRequest.AllMembers.
        var targets = await (
            from ps in db.PushSubscriptionRecords
            join om in db.OrgMembers on ps.UserId equals om.UserId
            where om.OrgId == request.OrgId
                && om.Status == "active"
                && (request.AllMembers
                    || om.Role == "admin" || om.Role == "secretariat" || om.Role == "finance")
                && (request.ActorUserId == null || ps.UserId != request.ActorUserId)
            select ps
        ).ToListAsync(ct);

        if (targets.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            title = request.Title,
            body = request.Body,
            url = request.Url,
        });

        var gone = new List<Data.Entities.PushSubscriptionRecord>();
        foreach (var sub in targets)
        {
            var keep = await sender.SendAsync(sub, payload, ct);
            if (!keep)
            {
                gone.Add(sub);
            }
        }

        if (gone.Count > 0)
        {
            db.PushSubscriptionRecords.RemoveRange(gone);
            await db.SaveChangesAsync(ct);
        }
    }
}
