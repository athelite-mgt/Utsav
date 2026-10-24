using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// One browser's Web Push registration on one device — the endpoint URL and
/// the two keys needed to encrypt a message to it (RFC 8291). Named
/// "Record" rather than "PushSubscription" to stay clear of
/// <see cref="global::WebPush.PushSubscription"/>, the wire type the WebPush
/// library itself sends to.
///
/// <see cref="Endpoint"/> is the natural key — unique per browser
/// installation regardless of which committee member is signed in on it —
/// so re-subscribing the same device just replaces the row rather than
/// accumulating duplicates (see PushEndpoints.SubscribeAsync).
/// </summary>
public partial class PushSubscriptionRecord
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public Guid UserId { get; set; }

    public string Endpoint { get; set; } = null!;

    public string P256dh { get; set; } = null!;

    public string Auth { get; set; } = null!;

    public string? UserAgent { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }

    public virtual User User { get; set; } = null!;
}
