using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;

namespace Utsav.Infrastructure.Audit;

/// <summary>
/// TASKLIST §5.3. Writes straight to Cosmos — never called directly from a
/// request thread; <see cref="QueuedAuditSink"/> is what request threads
/// depend on, and <see cref="AuditQueueProcessor"/> is what actually calls
/// this. Database/container names are config (<c>Cosmos:Database</c> /
/// <c>Cosmos:Container</c>), not hardcoded, so they can be renamed in the
/// portal without a code change.
/// </summary>
public sealed class CosmosAuditSink
{
    private readonly CosmosClient _client;
    private readonly string _databaseId;
    private readonly string _containerId;
    private readonly Container _container;

    public CosmosAuditSink(CosmosClient client, IConfiguration configuration)
    {
        _client = client;
        _databaseId = configuration["Cosmos:Database"]
            ?? throw new InvalidOperationException("Missing configuration: Cosmos:Database");
        _containerId = configuration["Cosmos:Container"]
            ?? throw new InvalidOperationException("Missing configuration: Cosmos:Container");

        _container = client.GetContainer(_databaseId, _containerId);
    }

    /// <summary>
    /// Idempotent startup bootstrap for the audit
    /// side: a fresh deployment for another committee gets its Cosmos
    /// database/container the same way it gets its SQL schema — no manual
    /// portal step — and it's a no-op against one that already exists. The
    /// audit account is expected to be serverless (asking a serverless account
    /// for RU/s throughput returns "Reading or replacing offers is not
    /// supported for serverless accounts"), so this never provisions
    /// dedicated throughput — doing so on a serverless account would fail
    /// outright, and there'd be a standing cost to reason about if it didn't.
    /// Partition key <c>/orgId</c>, no TTL — TASKLIST §5.3.
    /// </summary>
    public async Task EnsureContainerExistsAsync(CancellationToken ct = default)
    {
        var database = await _client.CreateDatabaseIfNotExistsAsync(_databaseId, cancellationToken: ct);
        await database.Database.CreateContainerIfNotExistsAsync(
            new ContainerProperties(_containerId, partitionKeyPath: "/orgId"), cancellationToken: ct);
    }

    public Task WriteAsync(AuditEntry entry, CancellationToken ct = default) =>
        _container.UpsertItemAsync(entry, new PartitionKey(entry.OrgId.ToString()), cancellationToken: ct);
}
