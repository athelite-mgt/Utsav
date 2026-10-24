using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Endpoints;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Layer four, tested on its own terms. Every test here deliberately goes
/// AROUND the application's defences: raw SQL, EF query filters switched off,
/// no chokepoint guard. If the database did not hold by itself, these would
/// show another committee's rows. They connect as the same least-privilege
/// login the deployed app uses.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class RlsBackstopTests(DatabaseFixture fixture)
{
    private static readonly string[] ExemptTables = ["sessions", "org_members", "api_keys"];

    private async Task<List<string>> ProtectedTablesAsync() => await fixture.QueryAsAdminAsync(
        "SELECT DISTINCT OBJECT_NAME(target_object_id) FROM sys.security_predicates ORDER BY 1");

    private static async Task<long> CountAsync(DbContext db, string sql)
    {
        // Opened through EF so the tenant interceptor runs, as in the app.
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<int> ExecuteAsync(DbContext db, string sql)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task The_set_of_protected_tables_is_exactly_every_org_scoped_table_but_the_three_exempt_ones()
    {
        // Ground truth from SQL Server's own catalog, compared against the
        // application guard's list: a table that carries org_id but was never
        // added to the security policy would be silently unprotected.
        var expected = TenancyGuard.OrgScopedTables
            .Where(t => !t.StartsWith("v_", StringComparison.Ordinal))
            .Except(ExemptTables, StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actual = (await ProtectedTablesAsync()).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

        Assert.Equal(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Every_protected_table_hides_the_other_committees_rows_from_raw_sql()
    {
        var leaks = new List<string>();
        foreach (var table in await ProtectedTablesAsync())
        {
            await using var asA = fixture.ContextFor(fixture.TenantA);
            await using var asB = fixture.ContextFor(fixture.TenantB);

            var totalA = await CountAsync(asA, $"SELECT COUNT(*) FROM [{table}]");
            var ownA = await CountAsync(asA, $"SELECT COUNT(*) FROM [{table}] WHERE org_id = '{fixture.TenantA}'");
            var foreignFromA = await CountAsync(asA, $"SELECT COUNT(*) FROM [{table}] WHERE org_id = '{fixture.TenantB}'");

            var totalB = await CountAsync(asB, $"SELECT COUNT(*) FROM [{table}]");
            var ownB = await CountAsync(asB, $"SELECT COUNT(*) FROM [{table}] WHERE org_id = '{fixture.TenantB}'");
            var foreignFromB = await CountAsync(asB, $"SELECT COUNT(*) FROM [{table}] WHERE org_id = '{fixture.TenantA}'");

            if (foreignFromA != 0 || foreignFromB != 0 || totalA != ownA || totalB != ownB)
            {
                leaks.Add($"{table} (A sees {totalA}/{ownA} own, {foreignFromA} foreign; B sees {totalB}/{ownB} own, {foreignFromB} foreign)");
            }
        }

        Assert.True(leaks.Count == 0, "Row-level security leaked: " + string.Join("; ", leaks));
    }

    [Fact]
    public async Task The_seeded_tables_really_do_hold_rows_for_both_committees()
    {
        // Guards the test above against passing vacuously on empty tables.
        await using var asA = fixture.ContextFor(fixture.TenantA);
        await using var asB = fixture.ContextFor(fixture.TenantB);

        foreach (var table in new[] { "donors", "subscriptions", "expenses", "vendors", "documents", "zones", "advances" })
        {
            Assert.True(await CountAsync(asA, $"SELECT COUNT(*) FROM [{table}]") > 0, $"{table} empty for A");
            Assert.True(await CountAsync(asB, $"SELECT COUNT(*) FROM [{table}]") > 0, $"{table} empty for B");
        }
    }

    [Fact]
    public async Task Rls_alone_holds_with_the_ef_query_filter_switched_off()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);

        var donors = await asA.Donors.IgnoreQueryFilters().ToListAsync();
        var expenses = await asA.Expenses.IgnoreQueryFilters().ToListAsync();

        Assert.NotEmpty(donors);
        Assert.All(donors, d => Assert.Equal(fixture.TenantA, d.OrgId));
        Assert.All(expenses, e => Assert.Equal(fixture.TenantA, e.OrgId));
    }

    [Fact]
    public async Task A_context_with_no_tenant_sees_nothing_even_with_filters_off()
    {
        await using var nobody = fixture.ContextWithNoTenant();

        Assert.Empty(await nobody.Donors.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await nobody.Expenses.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task A_forged_insert_into_another_committee_is_blocked_by_the_database()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);

        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(asA,
            $"INSERT INTO zones (id, org_id, name, sort_order) VALUES (NEWID(), '{fixture.TenantB}', 'PLANTED', 1)"));

        Assert.Equal(33504, error.Number);

        await using var asB = fixture.ContextFor(fixture.TenantB);
        Assert.Equal(0, await CountAsync(asB, "SELECT COUNT(*) FROM zones WHERE name = 'PLANTED'"));
    }

    [Fact]
    public async Task Moving_a_row_into_another_committee_is_blocked()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);

        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(asA,
            $"UPDATE zones SET org_id = '{fixture.TenantB}' WHERE id = '{fixture.A.Zone}'"));

        Assert.Equal(33504, error.Number);
    }

    [Fact]
    public async Task Updating_or_deleting_another_committees_row_affects_nothing()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);

        var updated = await ExecuteAsync(asA, $"UPDATE vendors SET name = 'HIJACKED' WHERE id = '{fixture.B.Vendor}'");
        var deleted = await ExecuteAsync(asA, $"DELETE FROM expenses WHERE id = '{fixture.B.Expense}'");

        Assert.Equal(0, updated);
        Assert.Equal(0, deleted);

        await using var asB = fixture.ContextFor(fixture.TenantB);
        Assert.Equal(1, await CountAsync(asB, $"SELECT COUNT(*) FROM vendors WHERE id = '{fixture.B.Vendor}' AND name = 'Vendor {DatabaseFixture.MarkerB}'"));
        Assert.Equal(1, await CountAsync(asB, $"SELECT COUNT(*) FROM expenses WHERE id = '{fixture.B.Expense}'"));
    }

    [Fact]
    public async Task The_application_login_cannot_disable_the_policy_it_runs_under()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);

        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(asA,
            "ALTER SECURITY POLICY dbo.TenantIsolationPolicy WITH (STATE = OFF)"));
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(asA,
            "DROP SECURITY POLICY dbo.TenantIsolationPolicy"));

        var policy = await fixture.QueryAsAdminAsync(
            "SELECT CAST(is_enabled AS int) FROM sys.security_policies WHERE name = 'TenantIsolationPolicy'");
        Assert.Equal(["1"], policy);
    }

    [Fact]
    public async Task The_application_login_holds_no_privileged_role()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);

        Assert.Equal(0, await CountAsync(asA, "SELECT CAST(IS_MEMBER('db_owner') AS int)"));
        Assert.Equal(0, await CountAsync(asA, "SELECT CAST(IS_MEMBER('db_ddladmin') AS int)"));
        // The deploy role is the one principal the predicate lets past; the
        // app login must never be in it, or RLS would not constrain the app.
        Assert.Equal(0, await CountAsync(asA, "SELECT CAST(IS_ROLEMEMBER('utsav_deploy_role') AS int)"));
    }

    [Fact]
    public async Task The_tenant_stamped_on_a_connection_cannot_be_repointed()
    {
        await using var asA = fixture.ContextFor(fixture.TenantA);

        // Whatever runs on this connection afterwards - a stored procedure, an
        // injected statement - cannot switch it to another committee.
        var error = await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync(asA,
            $"EXEC sp_set_session_context @key = N'TenantId', @value = '{fixture.TenantB}'"));

        Assert.Equal(15664, error.Number);
    }
}
