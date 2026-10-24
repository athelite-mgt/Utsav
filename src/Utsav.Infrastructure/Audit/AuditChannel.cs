using System.Threading.Channels;

namespace Utsav.Infrastructure.Audit;

/// <summary>
/// The hand-off between request threads and the one background consumer that
/// talks to Cosmos — TASKLIST §5.6: a slow or unreachable Cosmos account must
/// never add latency to, or fail, a committee member's save.
///
/// Bounded at 1000 so a stuck consumer can't grow this without bound; on
/// overflow the oldest queued entry is dropped rather than blocking the
/// writer, since blocking here would reintroduce exactly the latency this
/// exists to avoid. This app's real scale is ~10 concurrent requests
/// (TASKLIST §8.9), so overflow should never happen in practice — it's a
/// safety valve, not a sizing target.
/// </summary>
public sealed class AuditChannel
{
    private readonly Channel<AuditEntry> _channel = Channel.CreateBounded<AuditEntry>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelWriter<AuditEntry> Writer => _channel.Writer;

    public ChannelReader<AuditEntry> Reader => _channel.Reader;
}
