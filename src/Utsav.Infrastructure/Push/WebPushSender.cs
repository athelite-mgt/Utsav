using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Utsav.Infrastructure.Data.Entities;
using WebPush;

namespace Utsav.Infrastructure.Push;

/// <summary>
/// The one place that actually talks to a push service. There is no
/// third-party notification service behind this and nothing billed per
/// message — <see cref="WebPushClient"/> posts straight to whichever vendor
/// relay (Chrome→FCM, Safari→APNs, Firefox→Mozilla's own) issued the
/// subscription's endpoint URL, authenticating with the VAPID keypair
/// instead of an API key those relays would otherwise charge for.
/// </summary>
public sealed class WebPushSender(IOptions<VapidOptions> options, ILogger<WebPushSender> logger)
{
    private readonly VapidOptions _vapid = options.Value;
    private readonly WebPushClient _client = new();

    public bool IsConfigured => _vapid.IsConfigured;

    /// <summary>
    /// True if the caller should keep the subscription around (sent fine, or
    /// failed for a reason that might be transient); false if the push
    /// service itself says the subscription is dead and it should be deleted.
    /// </summary>
    public async Task<bool> SendAsync(PushSubscriptionRecord sub, string payloadJson, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return true;
        }

        var subscription = new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var vapidDetails = new VapidDetails(_vapid.Subject, _vapid.PublicKey, _vapid.PrivateKey);

        try
        {
            await _client.SendNotificationAsync(subscription, payloadJson, vapidDetails, ct);
            return true;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            // The relay itself is telling us this endpoint will never accept
            // another push — uninstalled, permission revoked, browser data
            // cleared. Not an error worth logging loudly; just stale bookkeeping.
            logger.LogInformation("Push subscription {Endpoint} is gone — removing it", sub.Endpoint);
            return false;
        }
        catch (Exception ex)
        {
            // Anything else (network blip, relay hiccup) is worth knowing
            // about but not worth deleting a subscription that might still
            // be good over — best-effort, same as the audit trail.
            logger.LogWarning(ex, "Push send failed for {Endpoint}", sub.Endpoint);
            return true;
        }
    }
}
