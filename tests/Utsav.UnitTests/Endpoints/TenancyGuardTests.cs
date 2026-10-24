using Microsoft.EntityFrameworkCore;
using Utsav.Api.Endpoints;
using Utsav.Infrastructure.Data;
using Xunit;

namespace Utsav.UnitTests.Endpoints;

public class TenancyGuardTests
{
    [Fact]
    public void Statement_against_an_org_scoped_table_without_a_predicate_is_refused()
    {
        var violation = Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("SELECT id, name FROM donors ORDER BY name", ["@year"]));

        Assert.Contains("donors", violation.Message);
    }

    [Fact]
    public void Statement_with_an_org_predicate_and_an_org_parameter_is_allowed()
    {
        TenancyGuard.AssertScoped(
            "SELECT id, name FROM donors WHERE org_id=@org ORDER BY name", ["@org"]);
    }

    [Fact]
    public void Predicate_without_the_bound_parameter_is_refused()
    {
        // A literal means the executor is not the one supplying the tenant,
        // which is the whole point of the guard.
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("SELECT id FROM donors WHERE org_id='...'", ["@org"]));
    }

    [Fact]
    public void Aliased_and_qualified_predicates_are_recognised()
    {
        TenancyGuard.AssertScoped(
            """
            SELECT m.id, u.email FROM org_members m JOIN users u ON u.id = m.user_id
             WHERE m.org_id=@org AND m.status='pending'
            """,
            ["@org"]);
    }

    [Fact]
    public void Reporting_views_are_treated_as_org_scoped()
    {
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("SELECT * FROM v_budget_vs_actual WHERE year_id=@year", ["@year"]));
    }

    [Fact]
    public void Tables_with_no_org_column_need_no_predicate()
    {
        // users is global identity; demanding org_id on it would be a false
        // positive that pushes developers to work around the guard, which is
        // worse than not having one.
        TenancyGuard.AssertScoped("SELECT id, email FROM users WHERE id=@id", ["@id"]);
    }

    [Fact]
    public void A_commented_out_predicate_does_not_satisfy_the_guard()
    {
        // Several inherited statements carry -- comments explaining their
        // joins. A mention of org_id=@org inside one must not count.
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped(
                "SELECT id FROM donors -- WHERE org_id=@org\n WHERE zone_id=@zone", ["@zone", "@org"]));
    }

    [Fact]
    public void A_table_name_inside_a_string_literal_is_not_a_table_reference()
    {
        // 'FROM donors' appearing in a literal must not make a global-table
        // statement look org-scoped (or hide a real one).
        TenancyGuard.AssertScoped("SELECT 'copied FROM donors' AS note, id FROM users WHERE id=@id", ["@id"]);
    }

    [Theory]
    [InlineData("UPDATE organizations SET name=@name")]
    [InlineData("UPDATE [organizations] SET accent_colour=@c WHERE slug=@slug")]
    [InlineData("DELETE FROM organizations")]
    [InlineData("UPDATE organizations SET name=@name WHERE id=@id")]
    public void A_statement_that_could_touch_every_committee_row_of_the_organizations_table_is_refused(string sql)
    {
        // organizations is the one table that carries no org_id (its own id is
        // the committee). A missing WHERE there would rename EVERY committee,
        // so the guard requires the statement to name the caller's committee.
        Assert.Throws<TenancyViolationException>(() => TenancyGuard.AssertScoped(sql, ["@name", "@c", "@slug", "@id", "@org"]));
    }

    [Fact]
    public void An_update_that_names_the_callers_committee_is_allowed()
    {
        TenancyGuard.AssertScoped("UPDATE organizations SET name=@name WHERE id=@org", ["@name", "@org"]);
        TenancyGuard.AssertScoped("UPDATE [organizations] SET logo_blob=@b WHERE [id] = @org", ["@b", "@org"]);
    }

    [Fact]
    public void Reading_the_organizations_table_is_not_restricted_by_this_rule()
    {
        // The directory of committees and sign-in both read it, unscoped, by design.
        TenancyGuard.AssertScoped("SELECT id, name FROM organizations WHERE slug=@slug", ["@slug"]);
    }

    [Fact]
    public void Guard_table_list_matches_the_ef_models_org_scoped_tables_exactly()
    {
        // The guard's list and the EF model must not drift apart: a new
        // org-scoped table that nobody adds here would be unprotected, and a
        // stale name here would be a false sense of coverage.
        var options = new DbContextOptionsBuilder<UtsavDbContext>()
            .UseSqlServer("Server=translation-only;Database=none;")
            .Options;
        using var db = new UtsavDbContext(options);

        var fromModel = db.Model.GetEntityTypes()
            .Where(e => e.FindProperty("OrgId") is not null)
            .Select(e => e.GetTableName() ?? e.GetViewName()!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = fromModel.Except(TenancyGuard.OrgScopedTables).OrderBy(n => n).ToList();
        var stale = TenancyGuard.OrgScopedTables.Except(fromModel).OrderBy(n => n).ToList();

        Assert.True(missing.Count == 0, $"In the model but not the guard: {string.Join(", ", missing)}");
        Assert.True(stale.Count == 0, $"In the guard but not the model: {string.Join(", ", stale)}");
    }
}
