using Microsoft.Data.SqlClient;
using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// Executes every report in <see cref="ToolCatalogue"/> against a real SQL
/// Server to prove the statements are valid: column names spelled correctly,
/// joins resolvable, parameters bound, T-SQL accepted by the engine.
///
/// <para>
/// The sibling <see cref="ToolCatalogueTests"/> only proves the SQL satisfies
/// <see cref="SqlGuard"/> — which a statement referencing a column that does
/// not exist would also do. Nothing but the database can answer "is this
/// actually runnable", and a report that throws the first time a treasurer
/// clicks it is the failure this closes.
/// </para>
///
/// <para>
/// Skipped unless <c>ASURA_LOCAL_SQL</c> holds a connection string, so CI and a
/// fresh clone stay hermetic and no credentials live in the repository. Point
/// it at a <b>local development database</b> — never production. The statements
/// are read-only, but the whole design principle here is that production is not
/// a test fixture.
/// </para>
/// </summary>
public class ToolCatalogueSqlExecutesTests
{
    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("ASURA_LOCAL_SQL");

    /// <summary>A fixed org id for the local-only Asura SQL checks; not special to the app.</summary>
    private static readonly Guid DevOrgId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static TheoryData<string> AllToolIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in ToolCatalogue.Tools.Keys)
        {
            data.Add(id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllToolIds))]
    public async Task Report_sql_is_valid_against_the_real_schema(string id)
    {
        var connectionString = ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // xunit 2.9 has no runtime skip, so an unconfigured environment
            // returns rather than failing. The assertions below are the point
            // of the test; nothing is asserted when there is no database to
            // assert against, and CI deliberately has none.
            return;
        }

        var tool = ToolCatalogue.Tools[id];

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(CancellationToken.None);

        // Same always-rollback discipline as ReadOnlyQueryExecutor, so even a
        // mistake in a statement under test cannot leave anything behind.
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            CancellationToken.None);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = tool.Sql;
            command.Transaction = transaction;
            command.CommandTimeout = 30;
            command.Parameters.Add(new SqlParameter("@orgId", DevOrgId));

            foreach (var p in tool.Parameters)
            {
                // Required parameters get a harmless well-typed value; optional
                // ones go in as NULL, which is exactly how the orchestrator
                // passes "not specified" and therefore the path worth testing.
                object value = p.Required
                    ? p.Type switch
                    {
                        "guid" => Guid.Empty,
                        "string" => "",
                        _ => DBNull.Value,
                    }
                    : DBNull.Value;

                command.Parameters.Add(new SqlParameter($"@{p.Name}", value));
            }

            await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);

            // Drain it: a deferred error (a bad expression in a projected
            // column) surfaces on read, not on ExecuteReader.
            while (await reader.ReadAsync(CancellationToken.None))
            {
            }

            // Every column the tool claims it will plot must really come back,
            // or the chart silently renders nothing.
            var returned = Enumerable.Range(0, reader.FieldCount)
                .Select(reader.GetName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (tool.LabelColumn is not null)
            {
                Assert.Contains(tool.LabelColumn, returned, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var column in tool.ValueColumns)
            {
                Assert.Contains(column, returned, StringComparer.OrdinalIgnoreCase);
            }

            if (tool.Drill is not null)
            {
                Assert.Contains(tool.Drill.FromColumn, returned, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (SqlException ex)
        {
            Assert.Fail($"{id} is not valid SQL against this schema: {ex.Message}");
        }
        finally
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }
}
