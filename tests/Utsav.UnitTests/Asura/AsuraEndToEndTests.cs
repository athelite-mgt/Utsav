using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Utsav.Api.Asura;
using Utsav.Infrastructure.Data;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// Drives the whole pipeline for real: live model, live database, real
/// orchestrator. Everything except the HTTP and session layer.
///
/// <para>
/// Every other Asura test uses stubs, which means none of them can catch the
/// failures that only appear when the real pieces meet — a provider rejecting
/// the request shape, a model naming a report that does not exist, the chosen
/// report failing to bind its parameters. This is the test that says the
/// feature works rather than that its parts do.
/// </para>
///
/// <para>
/// Requires <c>ASURA_LOCAL_SQL</c> (a <b>local</b> dev connection string) and at
/// least one of <c>GROQ_API_KEY</c> / <c>NVIDIA_API_KEY</c>. Returns without
/// asserting when they are absent, so CI stays hermetic and offline.
/// </para>
/// </summary>
public class AsuraEndToEndTests
{
    private static readonly Guid DevOrgId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private static (AsuraOrchestrator? Orchestrator, UtsavDbContext? Db) Build()
    {
        var sql = Environment.GetEnvironmentVariable("ASURA_LOCAL_SQL");
        var groq = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        var nvidia = Environment.GetEnvironmentVariable("NVIDIA_API_KEY");

        if (string.IsNullOrWhiteSpace(sql) || (string.IsNullOrWhiteSpace(groq) && string.IsNullOrWhiteSpace(nvidia)))
        {
            return (null, null);
        }

        var options = new AsuraOptions { Enabled = true };
        var tiers = new List<IChatModel>();

        foreach (var provider in options.Providers)
        {
            var key = provider.Provider == "groq" ? groq : nvidia;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            provider.ApiKey = key;
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(provider.TimeoutSeconds) };
            // The header the whole thing falls over without.
            http.DefaultRequestHeaders.UserAgent.ParseAdd(provider.UserAgent);
            tiers.Add(new OpenAiCompatibleChatModel(
                http, provider, NullLogger<OpenAiCompatibleChatModel>.Instance));
        }

        var model = new FallbackChatModel(tiers, NullLogger<FallbackChatModel>.Instance);

        var db = new UtsavDbContext(
            new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer(sql).Options);

        var executor = new ReadOnlyQueryExecutor(
            db, options, NullLogger<ReadOnlyQueryExecutor>.Instance);

        return (new AsuraOrchestrator(model, executor, options, NullLogger<AsuraOrchestrator>.Instance), db);
    }

    private static async Task<List<AsuraEvent>> AskAsync(AsuraOrchestrator orchestrator, string question)
    {
        var events = new List<AsuraEvent>();
        await orchestrator.AskAsync(question, DevOrgId, (e, _) =>
        {
            lock (events)
            {
                events.Add(e);
            }

            return Task.CompletedTask;
        }, CancellationToken.None);

        return events;
    }

    [Theory]
    [InlineData("Which zones are behind on collections this year?", "collections_by_zone")]
    [InlineData("Compare our expenses against the budget by category", "budget_vs_actual")]
    [InlineData("Who has been billed but hasn't paid yet?", "donor_dues")]
    public async Task Answers_a_real_question_by_choosing_the_right_report(string question, string expectedTool)
    {
        var (orchestrator, db) = Build();
        if (orchestrator is null)
        {
            return;
        }

        await using var _ = db!;
        var events = await AskAsync(orchestrator, question);

        var error = events.OfType<ErrorEvent>().FirstOrDefault();
        Assert.True(error is null, $"Asura refused: {error?.Message}");

        // Live narration is the feature, so its absence is a failure even if
        // the answer is right.
        Assert.NotEmpty(events.OfType<StepEvent>());

        var result = events.OfType<ResultEvent>().FirstOrDefault();
        Assert.NotNull(result);

        // Model choice is non-deterministic in principle; what must hold is
        // that it landed on the report a treasurer would expect.
        Assert.Equal(expectedTool, result.Provenance.ToolId);

        // Provenance is what lets a figure be defended, so it must be populated.
        Assert.NotEmpty(result.Provenance.Tables);
        Assert.NotEmpty(result.Provenance.Sql);

        Assert.Single(events.OfType<DoneEvent>());
    }

    [Fact]
    public async Task A_question_no_report_covers_is_refused_with_the_menu_rather_than_improvised()
    {
        var (orchestrator, db) = Build();
        if (orchestrator is null)
        {
            return;
        }

        await using var _ = db!;
        var events = await AskAsync(orchestrator,
            "What is the weather in Kolkata tomorrow, and write me a poem about it?");

        // No report covers this, and freeform SQL is not built. The honest
        // outcome is a refusal that names what Asura *can* do — never a
        // confident answer about data it does not have.
        var error = events.OfType<ErrorEvent>().FirstOrDefault();
        Assert.NotNull(error);
        Assert.True(error.Recoverable);
        Assert.Empty(events.OfType<ResultEvent>());
    }

    [Fact]
    public async Task A_drill_down_report_runs_from_its_id_without_a_model()
    {
        // The clickable-chart path: no language model involved at all, which is
        // what keeps a drillable chart from widening the attack surface.
        var sql = Environment.GetEnvironmentVariable("ASURA_LOCAL_SQL");
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        var options = new AsuraOptions();
        await using var db = new UtsavDbContext(
            new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer(sql).Options);
        var executor = new ReadOnlyQueryExecutor(db, options, NullLogger<ReadOnlyQueryExecutor>.Instance);

        var tool = ToolCatalogue.Tools["zone_donor_detail"];
        var result = await executor.ExecuteAsync(
            tool.Sql,
            DevOrgId,
            new Dictionary<string, object?> { ["zoneId"] = Guid.Empty, ["yearId"] = null },
            CancellationToken.None);

        Assert.Contains("donor", result.Columns, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_rejected_statement_never_reaches_the_database()
    {
        var sql = Environment.GetEnvironmentVariable("ASURA_LOCAL_SQL");
        if (string.IsNullOrWhiteSpace(sql))
        {
            return;
        }

        await using var db = new UtsavDbContext(
            new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer(sql).Options);
        var executor = new ReadOnlyQueryExecutor(
            db, new AsuraOptions(), NullLogger<ReadOnlyQueryExecutor>.Instance);

        // The executor validates at the door, so this is refused by SqlGuard
        // rather than attempted and failed by SQL Server.
        await Assert.ThrowsAsync<AsuraRefusedException>(() => executor.ExecuteAsync(
            "DELETE FROM donors WHERE org_id = @orgId", DevOrgId, null, CancellationToken.None));

        // And the table is still there.
        var after = await executor.ExecuteAsync(
            "SELECT COUNT(*) AS n FROM donors WHERE org_id = @orgId",
            DevOrgId, null, CancellationToken.None);
        Assert.Single(after.Rows);
    }
}
