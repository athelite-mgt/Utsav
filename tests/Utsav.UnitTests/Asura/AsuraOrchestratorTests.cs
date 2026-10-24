using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// The orchestrator's pure parsing helpers. These exist because real model
/// output is messier than the API contract suggests: providers vary in whether
/// they honour <c>response_format</c>, and reasoning models wrap their answer
/// in prose, a markdown fence, or a <c>&lt;think&gt;</c> block. Every shape
/// below was observed from one of the configured models during the
/// 2026-10-05 probe.
/// </summary>
public class AsuraOrchestratorTests
{
    [Fact]
    public void Reads_a_bare_json_object()
    {
        var json = AsuraOrchestrator.ExtractJson("""{"toolId":"donor_dues"}""");
        Assert.Contains("donor_dues", json);
    }

    [Fact]
    public void Reads_json_out_of_a_markdown_fence()
    {
        var json = AsuraOrchestrator.ExtractJson("""
            ```json
            {"toolId":"budget_vs_actual"}
            ```
            """);
        Assert.NotNull(json);
        Assert.StartsWith("{", json);
        Assert.Contains("budget_vs_actual", json);
    }

    [Fact]
    public void Reads_json_out_of_surrounding_prose()
    {
        // gpt-oss does this even when asked for JSON only.
        var json = AsuraOrchestrator.ExtractJson(
            """Sure! Here is the report you want: {"toolId":"vendor_spend"} — hope that helps.""");
        Assert.Equal("""{"toolId":"vendor_spend"}""", json);
    }

    [Fact]
    public void Discards_a_reasoning_block_before_looking_for_json()
    {
        // A <think> block can itself contain braces, which would otherwise be
        // picked up as the answer.
        var json = AsuraOrchestrator.ExtractJson("""
            <think>Maybe {"toolId":"wrong_guess"} fits? No, reconsider.</think>
            {"toolId":"collections_by_zone"}
            """);
        Assert.Equal("""{"toolId":"collections_by_zone"}""", json);
    }

    [Fact]
    public void Returns_null_when_there_is_no_object_at_all()
    {
        Assert.Null(AsuraOrchestrator.ExtractJson("I'm not sure what you mean."));
    }

    [Fact]
    public void Strips_reasoning_and_fences_from_prose_answers()
    {
        var text = AsuraOrchestrator.StripReasoning(
            "<think>internal deliberation</think>Collections are up 20%.");
        Assert.Equal("Collections are up 20%.", text);
        Assert.DoesNotContain("deliberation", text);
    }

    // ---- provenance --------------------------------------------------

    [Fact]
    public void Names_the_catalogued_tables_a_report_reads()
    {
        var tables = AsuraOrchestrator.TablesIn(ToolCatalogue.Tools["collections_by_zone"].Sql);

        Assert.Contains("zones", tables);
        Assert.Contains("donors", tables);
        Assert.Contains("subscriptions", tables);
        Assert.Contains("puja_years", tables);
    }

    [Fact]
    public void Leaves_cte_aliases_out_of_the_provenance_line()
    {
        // Every report joins a CTE named "y" to resolve the active year.
        // Listing it as a table read would be meaningless to a reader.
        var tables = AsuraOrchestrator.TablesIn(ToolCatalogue.Tools["donor_dues"].Sql);
        Assert.DoesNotContain("y", tables);
    }

    [Fact]
    public void Every_report_names_at_least_one_table_it_reads()
    {
        // Provenance is the feature that lets a treasurer defend a number, so
        // an empty list would quietly undermine the point of it.
        foreach (var tool in ToolCatalogue.Tools.Values)
        {
            Assert.NotEmpty(AsuraOrchestrator.TablesIn(tool.Sql));
        }
    }
}
