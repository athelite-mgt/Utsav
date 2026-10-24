using Microsoft.Extensions.Logging;

namespace Utsav.Infrastructure.Push;

/// <summary>
/// What the write path actually depends on (registered as
/// <see cref="IPushDispatcher"/>). Handing a request off is a synchronous,
/// in-memory <c>TryWrite</c> — it cannot be slow and cannot fail for a
/// push-service-side reason, because it never talks to one itself. See
/// <see cref="PushQueueProcessor"/> for the part that does.
/// </summary>
public sealed class QueuedPushDispatcher(PushChannel channel, ILogger<QueuedPushDispatcher> logger) : IPushDispatcher
{
    public Task EnqueueAsync(PushNotificationRequest request, CancellationToken ct = default)
    {
        if (!channel.Writer.TryWrite(request))
        {
            logger.LogWarning("Push queue full — dropped a notification: {Title}", request.Title);
        }

        return Task.CompletedTask;
    }
}
