using Microsoft.Extensions.Logging;

namespace Utsav.Infrastructure.Audit;

/// <summary>
/// What the write path actually depends on (registered as <see cref="IAuditSink"/>).
/// Handing an entry off is a synchronous, in-memory <c>TryWrite</c> — it
/// cannot be slow and cannot fail for a Cosmos-side reason, because it never
/// talks to Cosmos itself. See <see cref="AuditQueueProcessor"/> for the part
/// that does.
/// </summary>
public sealed class QueuedAuditSink(AuditChannel channel, ILogger<QueuedAuditSink> logger) : IAuditSink
{
    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        if (!channel.Writer.TryWrite(entry))
        {
            logger.LogWarning(
                "Audit queue full — dropped an entry for {EntityType}/{Action} at {Timestamp}",
                entry.EntityType, entry.Action, entry.Timestamp);
        }

        return Task.CompletedTask;
    }
}
