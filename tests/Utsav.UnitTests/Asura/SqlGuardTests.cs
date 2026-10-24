using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// Adversarial tests for the one check standing between a language model and
/// the committee's live database. Written to try to defeat it, not to confirm
/// it works on the happy path — a guard that has only ever been shown valid
/// input tells you nothing.
/// </summary>
public class SqlGuardTests
{
    private const string Org = "org_id = @orgId";

    // ---------------------------------------------------------------
    // Legitimate queries must pass. A guard that rejects real reports
    // is just as broken as one that admits a DROP, because the next
    // person fixes it by loosening it.
    // ---------------------------------------------------------------

    [Fact]
    public void Allows_a_plain_scoped_select()
    {
        var r = SqlGuard.Validate($"SELECT name, collected FROM subscriptions WHERE {Org}");
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void Allows_a_cte_because_a_leading_with_is_still_read_only()
    {
        // This is close to what the strongest benchmarked model actually
        // produced, and an earlier "must start with SELECT" rule rejected it.
        var sql = $"""
            WITH active_year AS (
                SELECT id FROM puja_years WHERE {Org} AND is_active = 1
            )
            SELECT z.name, SUM(s.collected) AS total
            FROM subscriptions s
            JOIN donors d ON d.id = s.donor_id
            JOIN zones z ON z.id = d.zone_id
            WHERE s.{Org} AND s.year_id IN (SELECT id FROM active_year)
            GROUP BY z.name
            """;
        var r = SqlGuard.Validate(sql);
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void Allows_multiple_ctes()
    {
        var sql = $"""
            WITH a AS (SELECT id FROM puja_years WHERE {Org}),
                 b AS (SELECT year_id, SUM(amount) amt FROM expenses WHERE {Org} GROUP BY year_id)
            SELECT * FROM a JOIN b ON b.year_id = a.id
            """;
        Assert.True(SqlGuard.Validate(sql).Ok);
    }

    [Fact]
    public void Allows_bracketed_identifiers()
    {
        var r = SqlGuard.Validate($"SELECT [name] FROM [donors] WHERE {Org}");
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void Allows_the_reporting_views()
    {
        var r = SqlGuard.Validate($"SELECT * FROM v_budget_vs_actual WHERE {Org}");
        Assert.True(r.Ok, r.Error);
    }

    [Theory]
    [InlineData("updated_at")]   // contains "update"
    [InlineData("created_on")]   // contains "create"
    [InlineData("deleted_flag")] // contains "delete"
    public void Allows_columns_whose_names_merely_contain_a_forbidden_word(string column)
    {
        // Whole-token matching matters: these column names are all over this
        // schema, so substring matching would reject nearly every real query.
        var r = SqlGuard.Validate($"SELECT {column} FROM subscriptions WHERE {Org}");
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void Allows_a_trailing_semicolon()
    {
        Assert.True(SqlGuard.Validate($"SELECT 1 FROM donors WHERE {Org};").Ok);
    }

    // ---------------------------------------------------------------
    // Mutations and DDL
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("INSERT INTO donors (name) VALUES ('x')")]
    [InlineData("UPDATE donors SET name = 'x'")]
    [InlineData("DELETE FROM donors")]
    [InlineData("DROP TABLE donors")]
    [InlineData("ALTER TABLE donors ADD c INT")]
    [InlineData("TRUNCATE TABLE donors")]
    [InlineData("CREATE TABLE t (c INT)")]
    [InlineData("MERGE donors USING x ON 1=1")]
    [InlineData("GRANT SELECT ON donors TO public")]
    [InlineData("BACKUP DATABASE puja TO DISK = 'x'")]
    [InlineData("SHUTDOWN")]
    public void Rejects_anything_that_is_not_a_select(string sql)
    {
        Assert.False(SqlGuard.Validate(sql).Ok);
    }

    [Fact]
    public void Rejects_select_into_which_creates_a_table_without_the_word_insert()
    {
        // The sneaky one: no INSERT, no UPDATE, yet it writes a new table.
        var r = SqlGuard.Validate($"SELECT * INTO stolen FROM donors WHERE {Org}");
        Assert.False(r.Ok);
    }

    [Fact]
    public void Rejects_waitfor_delay_which_turns_a_read_into_a_denial_of_service()
    {
        Assert.False(SqlGuard.Validate($"SELECT 1 FROM donors WHERE {Org}; WAITFOR DELAY '00:10:00'").Ok);
    }

    [Theory]
    [InlineData("EXEC sp_executesql N'SELECT 1'")]
    [InlineData("SELECT * FROM OPENROWSET('x','y','z')")]
    [InlineData("SELECT * FROM sys.objects WHERE org_id = @orgId")]
    [InlineData("SELECT * FROM information_schema.tables WHERE org_id = @orgId")]
    [InlineData("DBCC CHECKDB")]
    public void Rejects_procedure_and_metadata_access(string sql)
    {
        Assert.False(SqlGuard.Validate(sql).Ok);
    }

    // ---------------------------------------------------------------
    // Statement stacking, including via comments and literals
    // ---------------------------------------------------------------

    [Fact]
    public void Rejects_a_stacked_second_statement()
    {
        var r = SqlGuard.Validate($"SELECT 1 FROM donors WHERE {Org}; DROP TABLE donors");
        Assert.False(r.Ok);
        Assert.Contains("single statement", r.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_second_statement_hidden_behind_a_block_comment()
    {
        Assert.False(SqlGuard.Validate(
            $"SELECT 1 FROM donors WHERE {Org} /* harmless */ ; DROP TABLE donors").Ok);
    }

    [Fact]
    public void Allows_a_semicolon_and_a_drop_that_are_inside_a_line_comment()
    {
        // The comment is stripped, so what remains is one clean statement.
        // Rejecting this would be a false positive — models comment their SQL.
        var r = SqlGuard.Validate(
            $"SELECT name FROM donors WHERE {Org} -- ; DROP TABLE donors");
        Assert.True(r.Ok, r.Error);
        Assert.DoesNotContain("drop", r.Normalized!);
    }

    [Fact]
    public void Allows_a_semicolon_and_a_drop_that_are_inside_a_string_literal()
    {
        // A literal cannot change what the statement does, so its contents must
        // neither trip a rule nor smuggle one past. The naive regex approach
        // gets this exactly backwards.
        var r = SqlGuard.Validate(
            $"SELECT name FROM donors WHERE name = '; DROP TABLE donors --' AND {Org}");
        Assert.True(r.Ok, r.Error);
        Assert.DoesNotContain("drop", r.Normalized!);
    }

    [Fact]
    public void Allows_an_escaped_quote_inside_a_literal()
    {
        var r = SqlGuard.Validate(
            $"SELECT name FROM donors WHERE name = 'O''Brien' AND {Org}");
        Assert.True(r.Ok, r.Error);
    }

    [Fact]
    public void Rejects_a_comment_that_hides_the_rest_of_the_statement_unterminated()
    {
        Assert.False(SqlGuard.Validate($"SELECT 1 FROM donors WHERE {Org} /* never closed").Ok);
    }

    [Fact]
    public void Rejects_an_unterminated_string_literal()
    {
        Assert.False(SqlGuard.Validate($"SELECT 1 FROM donors WHERE name = 'oops AND {Org}").Ok);
    }

    [Fact]
    public void Handles_nested_block_comments_the_way_sql_server_does()
    {
        var r = SqlGuard.Validate($"SELECT 1 /* outer /* inner */ still outer */ FROM donors WHERE {Org}");
        Assert.True(r.Ok, r.Error);
    }

    // ---------------------------------------------------------------
    // Catalogue and scoping
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("users")]
    [InlineData("sessions")]
    [InlineData("api_keys")]
    [InlineData("push_subscriptions")]
    [InlineData("organizations")]
    public void Rejects_tables_outside_the_catalogue(string table)
    {
        var r = SqlGuard.Validate($"SELECT * FROM {table} WHERE org_id = @orgId");
        Assert.False(r.Ok);
        Assert.Contains("catalogue", r.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_an_excluded_table_disguised_in_brackets()
    {
        // Brackets are unwrapped during normalisation precisely so this cannot
        // slip past the catalogue check as punctuation.
        var r = SqlGuard.Validate("SELECT * FROM [users] WHERE org_id = @orgId");
        Assert.False(r.Ok);
    }

    [Fact]
    public void Rejects_an_excluded_table_reached_through_a_join()
    {
        var r = SqlGuard.Validate(
            $"SELECT d.name, u.password_hash FROM donors d JOIN users u ON u.id = d.id WHERE d.{Org}");
        Assert.False(r.Ok);
    }

    [Fact]
    public void Rejects_a_query_with_no_org_scope()
    {
        var r = SqlGuard.Validate("SELECT name FROM donors");
        Assert.False(r.Ok);
        Assert.Contains("@orgId", r.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_an_org_scope_written_as_a_literal_rather_than_a_parameter()
    {
        // A model that picks its own org id is a model that can read another
        // committee's books out of the shared production database.
        var r = SqlGuard.Validate(
            "SELECT name FROM donors WHERE org_id = '00000000-0000-0000-0000-000000000002'");
        Assert.False(r.Ok);
    }

    // ---------------------------------------------------------------
    // Shape
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_empty_input(string? sql)
    {
        Assert.False(SqlGuard.Validate(sql).Ok);
    }

    [Fact]
    public void Rejects_an_absurdly_long_statement()
    {
        var sql = "SELECT " + new string('a', 9_000) + $" FROM donors WHERE {Org}";
        Assert.False(SqlGuard.Validate(sql).Ok);
    }

    [Fact]
    public void Normalisation_collapses_whitespace_so_keywords_cannot_hide_across_newlines()
    {
        var (normalized, error) = SqlGuard.Normalize("SELECT\n\t  1   FROM\n donors");
        Assert.Null(error);
        Assert.Equal("select 1 from donors", normalized);
    }
}
