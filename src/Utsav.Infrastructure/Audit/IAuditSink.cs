namespace Utsav.Infrastructure.Audit;

/// <summary>
/// TASKLIST §5.1 — the one seam <c>DynamicSql</c> depends on, so the storage
/// behind the audit trail is a config/DI decision, not a code change at every
/// call site. The registered implementation is <see cref="QueuedAuditSink"/>,
/// which hands off to <see cref="CosmosAuditSink"/> on a background thread —
/// see TASKLIST §5.6.
/// </summary>
public interface IAuditSink
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
}
