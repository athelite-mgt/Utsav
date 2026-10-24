using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Utsav.Api.Asura;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Asura runs pre-written report SQL on a model's behalf, so a leak there is a
/// leak with an AI in the loop. The checks run every catalogued report against
/// the real database, and then run them again deliberately mis-scoped.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class AsuraIsolationTests(DatabaseFixture fixture)
{
    private static IReadOnlyDictionary<string, object?> ParametersFor(AsuraTool tool, TenantData tenant)
    {
        var values = new Dictionary<string, object?>();
        foreach (var parameter in tool.Parameters)
        {
            values[parameter.Name] = parameter.Name.ToLowerInvariant() switch
            {
                "yearid" => tenant.Year,
                "zoneid" => tenant.Zone,
                "collectorid" => tenant.Collector,
                "vendorid" => tenant.Vendor,
                _ => parameter.Type == "int" ? 1 : parameter.Type == "guid" ? tenant.Year : null,
            };
        }

        return values;
    }

    private static string Dump(QueryResult result) => JsonSerializer.Serialize(result.Rows);

    private ReadOnlyQueryExecutor ExecutorFor(Utsav.Infrastructure.Data.UtsavDbContext db) =>
        new(db, new AsuraOptions(), NullLogger<ReadOnlyQueryExecutor>.Instance);

    [Fact]
    public async Task Every_catalogued_report_runs_and_returns_only_the_callers_committee()
    {
        var problems = new List<string>();
        var reportsReturningOwnData = 0;
        foreach (var tool in ToolCatalogue.Tools.Values)
        {
            await using var db = fixture.ContextFor(fixture.TenantA);
            try
            {
                var result = await ExecutorFor(db).ExecuteAsync(tool.Sql, fixture.TenantA, ParametersFor(tool, fixture.A), default);
                var text = Dump(result);
                if (text.Contains(fixture.A.Marker, StringComparison.Ordinal))
                {
                    reportsReturningOwnData++;
                }

                if (text.Contains(fixture.B.Marker, StringComparison.Ordinal)
                    || text.Contains(fixture.B.Org.ToString(), StringComparison.OrdinalIgnoreCase)
                    || text.Contains(fixture.B.Year.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{tool.Id}: leaked committee B's data");
                }
            }
            catch (Exception e)
            {
                problems.Add($"{tool.Id}: {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // Without this the no-leak assertion above would pass on empty results.
        Assert.True(reportsReturningOwnData > 0, "no report returned any of committee A's seeded data");
    }

    [Fact]
    public async Task A_report_asked_for_the_wrong_committee_still_returns_nothing_of_theirs()
    {
        // The deliberate mistake: connected as A, but the executor is handed B's
        // org id (a bug in the orchestrator, or a prompt-injected parameter).
        // The report's own @orgId predicate would then ask for B's rows, and
        // only row-level security stands in the way.
        var problems = new List<string>();
        foreach (var tool in ToolCatalogue.Tools.Values)
        {
            await using var db = fixture.ContextFor(fixture.TenantA);
            try
            {
                var result = await ExecutorFor(db).ExecuteAsync(tool.Sql, fixture.TenantB, ParametersFor(tool, fixture.B), default);
                var text = Dump(result);
                if (text.Contains(fixture.B.Marker, StringComparison.Ordinal))
                {
                    problems.Add($"{tool.Id}: returned committee B's data to a session scoped to A");
                }
            }
            catch (Exception e)
            {
                problems.Add($"{tool.Id}: {e.GetType().Name}: {e.Message}");
            }
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void The_catalogue_is_not_empty()
    {
        // Guards the two tests above against passing vacuously.
        Assert.True(ToolCatalogue.Tools.Count >= 10, $"only {ToolCatalogue.Tools.Count} reports in the catalogue");
    }
}
