using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Utsav.Infrastructure.Data;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Deployment applies the schema itself, so "start the new version" is the only step. That has to
/// be safe to repeat (every restart runs it), safe when two instances start together (scale-out),
/// and must produce the row-level-security policy the whole isolation model depends on.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class DatabaseMigratorTests(DatabaseFixture fixture)
{
    private async Task<string> CreateEmptyDatabaseAsync(string name)
    {
        await using var master = new SqlConnection(fixture.SaConnection("master"));
        await master.OpenAsync();
        await using var create = master.CreateCommand();
        create.CommandText = $"CREATE DATABASE [{name}];";
        await create.ExecuteNonQueryAsync();
        return fixture.SaConnection(name);
    }

    private async Task DropAsync(string name)
    {
        SqlConnection.ClearAllPools();
        await using var master = new SqlConnection(fixture.SaConnection("master"));
        await master.OpenAsync();
        await using var drop = master.CreateCommand();
        drop.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(string connection, string sql)
    {
        await using var c = new SqlConnection(connection);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task An_empty_database_gets_the_schema_and_the_security_policy_and_a_second_run_changes_nothing()
    {
        var name = $"utsav_mig_{Guid.NewGuid():N}";
        var connection = await CreateEmptyDatabaseAsync(name);
        try
        {
            await DatabaseMigrator.MigrateAsync(connection, NullLogger.Instance);
            await DatabaseMigrator.MigrateAsync(connection, NullLogger.Instance); // every restart runs it again

            await using var db = new UtsavDbContext(new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer(connection).Options);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal(db.Database.GetMigrations().Count(),
                await ScalarAsync(connection, "SELECT COUNT(*) FROM __EFMigrationsHistory"));
            Assert.True(await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.security_policies WHERE is_enabled = 1") > 0,
                "the row-level-security policy must exist and be enabled");
            Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM sys.database_principals WHERE name = N'utsav_app_role'"));
        }
        finally
        {
            await DropAsync(name);
        }
    }

    [Fact]
    public async Task Two_instances_starting_at_once_both_succeed()
    {
        var name = $"utsav_mig_{Guid.NewGuid():N}";
        var connection = await CreateEmptyDatabaseAsync(name);
        try
        {
            // App Service can start several instances together when it scales out.
            await Task.WhenAll(
                DatabaseMigrator.MigrateAsync(connection, NullLogger.Instance),
                DatabaseMigrator.MigrateAsync(connection, NullLogger.Instance));

            await using var db = new UtsavDbContext(new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer(connection).Options);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            await DropAsync(name);
        }
    }

    [Fact]
    public async Task The_privilege_check_flags_an_owner_login_and_passes_the_least_privilege_one()
    {
        var owner = await DatabaseMigrator.InspectRuntimePrivilegesAsync(fixture.SaConnection(new SqlConnectionStringBuilder(fixture.AppConnection()).InitialCatalog));
        var app = await DatabaseMigrator.InspectRuntimePrivilegesAsync(fixture.AppConnection());

        Assert.True(owner.IsPrivileged);
        Assert.False(app.IsPrivileged);
    }
}
