using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

public class ToolCatalogueTests
{
    public static TheoryData<string> AllToolIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in ToolCatalogue.Tools.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    /// <summary>
    /// A report whose own SQL the guard would reject is a report that can never
    /// run — a treasurer would click it and get a refusal. Catching that here
    /// makes it a build failure instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllToolIds))]
    public void Every_report_passes_the_sql_guard(string id)
    {
        var tool = ToolCatalogue.Tools[id];
        var verdict = SqlGuard.Validate(tool.Sql);
        Assert.True(verdict.Ok, $"{id}: {verdict.Error}");
    }

    [Theory]
    [MemberData(nameof(AllToolIds))]
    public void Every_report_is_scoped_to_the_organisation(string id)
    {
        Assert.Contains("@orgId", ToolCatalogue.Tools[id].Sql);
    }

    [Theory]
    [MemberData(nameof(AllToolIds))]
    public void Every_report_declares_the_parameters_its_sql_uses(string id)
    {
        var tool = ToolCatalogue.Tools[id];
        var declared = tool.Parameters.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // @orgId is stamped by the executor from the session, never declared.
        foreach (var used in System.Text.RegularExpressions.Regex
                     .Matches(tool.Sql, @"@([a-zA-Z_][a-zA-Z0-9_]*)")
                     .Select(m => m.Groups[1].Value)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Where(p => !p.Equals("orgId", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.True(declared.Contains(used),
                $"{id} uses @{used} but does not declare it, so it would fail at bind time.");
        }
    }

    /// <summary>
    /// A drill-down that points at a report which does not exist is a dead
    /// click in the UI. Both ends are checked: the target report, and the
    /// parameter it expects.
    /// </summary>
    [Fact]
    public void Every_drill_down_points_at_a_report_that_exists_and_accepts_its_parameter()
    {
        foreach (var tool in ToolCatalogue.Tools.Values.Where(t => t.Drill is not null))
        {
            var drill = tool.Drill!;

            Assert.True(ToolCatalogue.TryGet(drill.ToolId, out var target),
                $"{tool.Id} drills into '{drill.ToolId}', which is not a report.");

            Assert.Contains(drill.ParamName, target.Parameters.Select(p => p.Name));
        }
    }

    /// <summary>
    /// The clicked row has to actually carry the column the drill reads its
    /// parameter from, or the UI sends null and the detail report returns
    /// nothing. The column must therefore be selected with that exact alias.
    /// </summary>
    [Fact]
    public void Every_drill_down_reads_a_column_its_parent_report_selects()
    {
        foreach (var tool in ToolCatalogue.Tools.Values.Where(t => t.Drill is not null))
        {
            var drill = tool.Drill!;
            Assert.True(
                tool.Sql.Contains($"AS {drill.FromColumn}", StringComparison.OrdinalIgnoreCase)
                || tool.Sql.Contains($" {drill.FromColumn},", StringComparison.OrdinalIgnoreCase),
                $"{tool.Id} drills using column '{drill.FromColumn}', which it does not select under that alias.");
        }
    }

    [Theory]
    [MemberData(nameof(AllToolIds))]
    public void Every_report_that_declares_a_chart_names_the_columns_to_plot(string id)
    {
        var tool = ToolCatalogue.Tools[id];
        if (tool.Chart == "none")
        {
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(tool.LabelColumn), $"{id} has a chart but no label column.");
        Assert.NotEmpty(tool.ValueColumns);
    }

    [Theory]
    [MemberData(nameof(AllToolIds))]
    public void Chart_kinds_are_restricted_to_what_the_ui_can_render(string id)
    {
        Assert.Contains(ToolCatalogue.Tools[id].Chart, new[] { "bar", "line", "none" });
    }

    [Fact]
    public void The_menu_offered_to_the_model_never_contains_sql()
    {
        // The model picks a report by id; it is never shown the statement. If
        // the SQL leaked into the menu, a prompt-injected row could try to talk
        // the model into rewriting it.
        var menu = ToolCatalogue.Menu();

        Assert.DoesNotContain("SELECT", menu, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FROM", menu, StringComparison.OrdinalIgnoreCase);

        foreach (var id in ToolCatalogue.Tools.Keys)
        {
            Assert.Contains(id, menu);
        }
    }

    [Fact]
    public void Unknown_report_ids_are_refused_rather_than_guessed_at()
    {
        Assert.False(ToolCatalogue.TryGet("drop_everything", out _));
        Assert.False(ToolCatalogue.TryGet(null, out _));
    }
}
