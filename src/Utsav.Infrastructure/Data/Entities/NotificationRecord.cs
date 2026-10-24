using System;

namespace Utsav.Infrastructure.Data.Entities;

/// <summary>
/// The durable record behind the in-app Notifications page — one row per
/// notification event (a transaction update or an admin announcement), not
/// per device it was pushed to. Logged whether or not Web Push actually
/// delivered, so the page is a reliable history even for members who never
/// turned push on. Named "…Record" to avoid colliding with any ambient
/// Notification type, same as <see cref="PushSubscriptionRecord"/>.
/// </summary>
public partial class NotificationRecord
{
    public Guid Id { get; set; }

    public Guid? OrgId { get; set; }

    public string Title { get; set; } = null!;

    public string Body { get; set; } = null!;

    /// <summary>In-app path this notification points at, e.g. "/expenses". The Notifications page routes here on click.</summary>
    public string Url { get; set; } = null!;

    /// <summary>"transaction" or "announcement" — drives the icon and audience note on the page.</summary>
    public string Kind { get; set; } = null!;

    /// <summary>Who triggered it (the editor, or the admin who announced). Null if unknown.</summary>
    public Guid? ActorUserId { get; set; }

    public string? ActorName { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Organization? Org { get; set; }
}
