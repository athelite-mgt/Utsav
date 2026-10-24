using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Utsav.Infrastructure.Data;

/// <summary>What the running application's database login is allowed to do to the schema.</summary>
public sealed record RuntimePrivileges(bool IsDbOwner, bool CanAlterSecurityPolicy)
{
    /// <summary>
    /// Row-level security only backstops the application if the application cannot switch it off.
    /// </summary>
    public bool IsPrivileged => IsDbOwner || CanAlterSecurityPolicy;
}

/// <summary>
/// Brings the schema up to date as part of starting the app, so a deployment is a single step.
///
/// Schema changes need rights the running application must not have (it must not be able to drop the
/// row-level-security policy), so this takes its own connection string. The pipeline gives the app a
/// separate migration login for the startup moment; the request-serving connection stays least-privilege.
/// EF Core takes a database lock around migration, so instances started together (scale-out) queue up
/// rather than collide.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task MigrateAsync(string connectionString, ILogger logger, CancellationToken ct = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        // A serverless database that has paused takes a while to wake; do not fail the start over it.
        builder.ConnectTimeout = Math.Max(builder.ConnectTimeout, 60);

        await using var db = new UtsavDbContext(new DbContextOptionsBuilder<UtsavDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql
                .EnableRetryOnFailure(maxRetryCount: 5)
                .CommandTimeout(300))
            .Options);

        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        logger.LogInformation("Applying {Count} database migration(s): {Names}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
        logger.LogInformation("Database migrations applied.");
    }

    /// <summary>Asks the database what the given (runtime) login may do. Diagnostic, never throws for a bad login.</summary>
    public static async Task<RuntimePrivileges> InspectRuntimePrivilegesAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
              CAST(ISNULL(IS_ROLEMEMBER(N'db_owner'), 0) AS int),
              CAST(ISNULL(HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY SECURITY POLICY'), 0) AS int);
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new RuntimePrivileges(reader.GetInt32(0) == 1, reader.GetInt32(1) == 1);
    }
}
