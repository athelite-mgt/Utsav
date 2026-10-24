using System.Collections.Concurrent;
using Utsav.Infrastructure.Audit;

namespace Utsav.IsolationTests.Support;

/// <summary>
/// Stands in for Cosmos so the suite can run offline, and lets tests assert
/// which tenant each audit event was filed under: the real container is
/// partitioned on /orgId, so a wrong value there files one committee's audit
/// trail in another's partition.
/// </summary>
public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public IReadOnlyCollection<AuditEntry> Entries => _entries.ToArray();

    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default)
    {
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }
}
