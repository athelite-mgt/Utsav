using Microsoft.Extensions.Logging;

namespace Utsav.Infrastructure.Audit;

/// <summary>
/// Writes audit events to the application log instead of Cosmos.
///
/// This exists so the app can run on a developer's laptop, or in an offline
/// test, without a Cosmos account. It is NOT a quiet fallback: it is only used
/// when <c>Audit:Mode</c> is set to <c>Log</c> explicitly. The default is
/// Cosmos, so a production deployment that is missing its Cosmos configuration
/// fails to start instead of running with no audit trail.
/// </summary>
public sealed class LoggingAuditSink(ILogger<LoggingAuditSink> logger) : IAuditSink
{
    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        logger.LogInformation(
            "AUDIT {Outcome} {Action} {Entity} org={OrgId} user={User} role={Role} {Method} {Path}",
            entry.Outcome, entry.Action, entry.EntityType, entry.OrgId, entry.UserEmail, entry.UserRole,
            entry.RequestMethod, entry.RequestPath);

        return Task.CompletedTask;
    }
}
