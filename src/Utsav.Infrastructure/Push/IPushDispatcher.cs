namespace Utsav.Infrastructure.Push;

/// <summary>
/// The one seam <c>DynamicSql</c> depends on — mirrors
/// <see cref="Utsav.Infrastructure.Audit.IAuditSink"/>'s role for the audit
/// trail. The registered implementation is <see cref="QueuedPushDispatcher"/>,
/// which hands off to <see cref="PushQueueProcessor"/> on a background thread.
/// </summary>
public interface IPushDispatcher
{
    Task EnqueueAsync(PushNotificationRequest request, CancellationToken ct = default);
}
