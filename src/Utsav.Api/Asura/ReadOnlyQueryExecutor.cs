using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;

namespace Utsav.Api.Asura;

internal sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    bool Truncated,
    long ElapsedMs);

/// <summary>
/// Runs a validated SELECT against the committee's database and guarantees it
/// cannot change anything. Layers 3 and 4 of Asura's four safety layers.
///
/// <para>
/// <b>Layer 3 — the transaction is always rolled back.</b> Every query runs
/// inside an explicit transaction that is rolled back unconditionally, on the
/// success path as well as the failure path. Nothing commits, ever. This is
/// what lets the feature be called read-only with real confidence rather than
/// hopefully: even a statement that somehow defeated
/// <see cref="AsuraCatalog"/> and <see cref="SqlGuard"/> would leave no trace.
/// Azure SQL offers no read-only connection flag for a primary replica, so this
/// is the strongest guarantee available in code, and it is cheap.
/// </para>
///
/// <para>
/// <b>Layer 4 — caps.</b> A command timeout and a row limit, so a careless
/// aggregate cannot become a denial of service against the committee's own app
/// while the puja is running.
/// </para>
/// </summary>
internal sealed class ReadOnlyQueryExecutor(
    UtsavDbContext db,
    AsuraOptions options,
    ILogger<ReadOnlyQueryExecutor> log)
{
    /// <summary>
    /// Validates, then executes. Validation happens here as well as in the
    /// orchestrator on purpose: this is the only code path to the database, so
    /// checking at the door means no future caller can route around the guard
    /// by forgetting to call it. Belt and braces, deliberately duplicated.
    /// </summary>
    public async Task<QueryResult> ExecuteAsync(
        string sql, Guid orgId, IReadOnlyDictionary<string, object?>? parameters, CancellationToken ct)
    {
        var verdict = SqlGuard.Validate(sql);
        if (!verdict.Ok)
        {
            throw new AsuraRefusedException(verdict.Error ?? "Query rejected.");
        }

        var started = System.Diagnostics.Stopwatch.StartNew();

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        // Not EF's BeginTransactionAsync: this executor deliberately owns a
        // transaction that is never handed to anyone and never committed, and
        // keeping it off the DbContext means it cannot be accidentally
        // enlisted in — or committed by — unrelated application code sharing
        // the same context.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, ct);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = verdict.Normalized!;
            command.Transaction = transaction;
            command.CommandTimeout = options.QueryTimeoutSeconds;

            command.Parameters.Add(new SqlParameter("@orgId", orgId));
            if (parameters is not null)
            {
                foreach (var (name, value) in parameters)
                {
                    var bare = name.TrimStart('@');
                    if (string.Equals(bare, "orgId", StringComparison.OrdinalIgnoreCase))
                    {
                        // The organisation scope is stamped from the caller's
                        // session, never accepted from a tool's parameters —
                        // the same rule TableRegistry enforces for org_id on
                        // writes, for the same reason.
                        continue;
                    }

                    command.Parameters.Add(new SqlParameter($"@{bare}", value ?? DBNull.Value));
                }
            }

            var columns = new List<string>();
            var rows = new List<IReadOnlyDictionary<string, object?>>();
            var truncated = false;

            await using var reader = await command.ExecuteReaderAsync(ct);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            while (await reader.ReadAsync(ct))
            {
                if (rows.Count >= options.MaxRows)
                {
                    // Capped while reading rather than by rewriting the SQL to
                    // add TOP. Rewriting looks tidier but is fragile: the final
                    // SELECT of a CTE chain is not the first one in the text,
                    // so the injection either lands in the wrong place or has
                    // to re-parse the statement. Stopping the reader needs no
                    // parsing and cannot corrupt a valid query. The command
                    // timeout bounds the server-side work either way.
                    truncated = true;
                    break;
                }

                var row = new Dictionary<string, object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct)
                        ? null
                        : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return new QueryResult(columns, rows, truncated, started.ElapsedMilliseconds);
        }
        finally
        {
            // The whole point. Rolled back in a finally so it happens on every
            // path — success, exception, cancellation — and is never skipped.
            try
            {
                await transaction.RollbackAsync(ct);
            }
            catch (Exception ex)
            {
                // A rollback that fails is worth knowing about but must not
                // replace whatever the caller was already dealing with. The
                // transaction is disposed either way, which rolls it back
                // anyway.
                log.LogWarning(ex, "Asura: rolling back the read-only transaction failed");
            }
        }
    }
}

/// <summary>
/// Asura declined to answer — a rejected query, an unknown report, a rate
/// limit. Carries a message intended to be shown to the person who asked,
/// rather than a stack trace.
/// </summary>
internal sealed class AsuraRefusedException(string message) : Exception(message);
