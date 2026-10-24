using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Utsav.Infrastructure;
using Utsav.Infrastructure.Audit;

namespace Utsav.UnitTests.Audit;

/// <summary>
/// Cosmos is the audit store, and it is the DEFAULT: a production deployment
/// missing its Cosmos configuration must fail to start, never quietly run with
/// no audit trail. Running without Cosmos (a developer's laptop, an offline
/// test run) is something you ask for explicitly with Audit:Mode=Log.
/// </summary>
public class AuditModeTests
{
    private static ServiceCollection Register(params (string Key, string? Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(settings.Select(s => KeyValuePair.Create(s.Key, s.Value)))
            {
                ["ConnectionStrings:AzureSql"] = "Server=none;Database=none;",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUtsavInfrastructure(configuration);
        return services;
    }

    [Fact]
    public void By_default_audit_goes_to_cosmos_through_the_queue_and_worker()
    {
        var services = Register();

        Assert.Contains(services, d => d.ServiceType == typeof(CosmosClient));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AuditQueueProcessor));
        Assert.Equal(typeof(QueuedAuditSink), services.Single(d => d.ServiceType == typeof(IAuditSink)).ImplementationType);
    }

    [Fact]
    public void By_default_a_missing_cosmos_configuration_fails_loudly_rather_than_dropping_audit()
    {
        var provider = Register().BuildServiceProvider();

        // Resolving the store without its configuration throws; it does not
        // degrade into something that silently discards the trail.
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<CosmosClient>());
    }

    [Fact]
    public void Log_mode_is_an_explicit_opt_in_that_needs_no_cosmos_at_all()
    {
        var services = Register(("Audit:Mode", "Log"));
        var provider = services.BuildServiceProvider();

        Assert.IsType<LoggingAuditSink>(provider.GetRequiredService<IAuditSink>());
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(CosmosClient));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(AuditQueueProcessor));
    }

    [Theory]
    [InlineData("cosmos")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_other_than_log_means_cosmos(string? mode)
    {
        var services = Register(("Audit:Mode", mode));

        Assert.Contains(services, d => d.ServiceType == typeof(CosmosClient));
    }

    [Fact]
    public async Task The_logging_sink_accepts_an_entry_without_throwing()
    {
        var sink = new LoggingAuditSink(Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggingAuditSink>.Instance);
        var entry = new AuditEntry(
            Id: "1", OrgId: Guid.NewGuid(), Timestamp: DateTimeOffset.UtcNow, UserId: null, UserEmail: "a@b.c",
            UserFullName: null, UserRole: "admin", Action: "INSERT INTO", EntityType: "expenses", EntityLabel: null,
            Before: null, After: null, Outcome: AuditEntry.Success, ErrorMessage: null, RequestMethod: "POST",
            RequestPath: "/api/expenses", IpAddress: null, UserAgent: null, CorrelationId: "c");

        await sink.WriteAsync(entry);
    }
}
