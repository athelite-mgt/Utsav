namespace Utsav.Infrastructure.Push;

/// <summary>
/// Everything <see cref="PushQueueProcessor"/> needs to fan a notification
/// out to the right devices — one payload, sent to every subscription that
/// matches. Built once by <see cref="Utsav.Api.Endpoints.DynamicSql"/> right
/// after a transaction-table write succeeds.
/// </summary>
public sealed record PushNotificationRequest(
    Guid? OrgId,
    /// <summary>Whoever made the change — excluded from the fan-out. Nobody needs to be told about their own edit.</summary>
    Guid? ActorUserId,
    string Title,
    string Body,
    string Url,
    /// <summary>
    /// False (default) for every existing transaction-update call site —
    /// Admin/Secretariat/Finance only, never Executive or Viewer. True only for
    /// an admin-authored announcement (<c>PushEndpoints.AnnounceAsync</c>),
    /// which is meant for every active member regardless of role.
    /// </summary>
    bool AllMembers = false,
    /// <summary>Display name of the actor, for the in-app Notifications log.</summary>
    string? ActorName = null,
    /// <summary>"transaction" or "announcement" — logged for the Notifications page.</summary>
    string Kind = "transaction");
