using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Utsav.Infrastructure.Audit;

/// <summary>
/// Drains <see cref="AuditChannel"/> for the life of the app and writes each
/// entry to Cosmos via <see cref="CosmosAuditSink"/>. One bad write (a
/// transient Cosmos error, a malformed entry) is logged and skipped rather
/// than taking the whole loop down — TASKLIST §5.6: the audit trail's own
/// failures must stay contained to the audit trail.
/// </summary>
public sealed class AuditQueueProcessor(AuditChannel channel, CosmosAuditSink sink, ILogger<AuditQueueProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var entry in channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await sink.WriteAsync(entry, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Failed to write audit entry {Id} ({EntityType}/{Action}) to Cosmos",
                    entry.Id, entry.EntityType, entry.Action);
            }
        }
    }
}
