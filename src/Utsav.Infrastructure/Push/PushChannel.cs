using System.Threading.Channels;

namespace Utsav.Infrastructure.Push;

/// <summary>
/// The hand-off between request threads and the one background consumer
/// that actually talks to the push services — same shape and same reasoning
/// as <see cref="Utsav.Infrastructure.Audit.AuditChannel"/>: a slow or
/// unreachable push relay must never add latency to, or fail, a committee
/// member's save.
///
/// Bounded at 1000 for the same reason as the audit channel — a safety
/// valve sized well above this app's real scale, not a sizing target.
/// </summary>
public sealed class PushChannel
{
    private readonly Channel<PushNotificationRequest> _channel = Channel.CreateBounded<PushNotificationRequest>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelWriter<PushNotificationRequest> Writer => _channel.Writer;

    public ChannelReader<PushNotificationRequest> Reader => _channel.Reader;
}
