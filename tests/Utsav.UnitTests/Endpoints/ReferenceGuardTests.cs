using Utsav.Api.Endpoints;
using Xunit;

namespace Utsav.UnitTests.Endpoints;

public class ReferenceGuardTests
{
    /// <summary>
    /// Columns ending in _id that are deliberately not checked against a
    /// tenant table: users are global identity rather than committee data.
    /// </summary>
    private static readonly string[] Unchecked = ["user_id"];

    [Fact]
    public void Every_foreign_key_column_the_generic_crud_routes_accept_is_checked()
    {
        // A new *_id column added to the registry without a mapping here would
        // let a record reference another committee's row. This fails instead.
        var unmapped = TableRegistry.Tables
            .SelectMany(t => t.Value.Select(column => (Table: t.Key, Column: column)))
            .Where(x => x.Column.EndsWith("_id", StringComparison.Ordinal))
            .Where(x => !Unchecked.Contains(x.Column) && ReferenceGuard.TableFor(x.Column) is null)
            .Select(x => $"{x.Table}.{x.Column}")
            .ToList();

        Assert.True(unmapped.Count == 0, "Unchecked foreign-key columns: " + string.Join(", ", unmapped));
    }

    [Theory]
    [InlineData("vendor_id", "vendors")]
    [InlineData("@year_id", "puja_years")]
    [InlineData("collector_id", "collectors")]
    [InlineData("assignee_collector_id", "collectors")]
    [InlineData("year", "puja_years")]
    [InlineData("@vendor", "vendors")]
    public void Parameter_names_resolve_to_the_table_their_id_must_belong_to(string parameter, string table)
    {
        Assert.Equal(table, ReferenceGuard.TableFor(parameter));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("org")]
    [InlineData("amount")]
    [InlineData("user_id")]
    public void Other_parameters_are_not_treated_as_references(string parameter)
    {
        Assert.Null(ReferenceGuard.TableFor(parameter));
    }
}
