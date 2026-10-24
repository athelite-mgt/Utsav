using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Utsav.Api.Endpoints;
using Utsav.Infrastructure.Authentication;
using AuditEntry = Utsav.Infrastructure.Audit.AuditEntry;

namespace Utsav.UnitTests.Audit;

/// <summary>
/// <see cref="DynamicSql"/> is the one chokepoint every mutation in the app
/// goes through (27 call sites — TASKLIST §5), so its audit capture is
/// tested against real SQL text lifted from those call sites rather than
/// invented examples: a future template that doesn't match the assumed
/// "verb, then table, optionally bracketed" shape should fail a test here,
/// not silently stop being audited.
///
/// <see cref="DynamicSql.ExecuteAsync"/> itself (the DB round trip and the
/// actual Cosmos write) needs a real SQL Server and a real Cosmos account —
/// out of scope for this project's unit tests today (TASKLIST §8.3 is still
/// unchecked for every part of the app, not just this feature). What's
/// tested here is everything around that I/O: parsing, parameter shaping,
/// and entry assembly, all of which is pure given its inputs.
/// </summary>
public class DynamicSqlAuditTests
{
    public class ParseVerbAndTable
    {
        [Theory]
        // Generic CRUD (CrudEndpoints) — bracketed, table name resolved from TableRegistry at runtime.
        [InlineData("DELETE FROM [expenses] WHERE id = @id AND org_id = @org;", "DELETE FROM", "expenses")]
        [InlineData("UPDATE [advances] SET [status] = @p0 WHERE id = @id AND org_id = @org;", "UPDATE", "advances")]
        [InlineData("INSERT INTO [donors] (id, org_id, name) VALUES (@id, @org, @p0);", "INSERT INTO", "donors")]
        // The detach-before-delete UPDATE (CrudEndpoints.DeleteAsync).
        [InlineData("UPDATE [workflow_items] SET [owner_collector_id] = NULL WHERE [owner_collector_id] = @id AND org_id = @org;", "UPDATE", "workflow_items")]
        // Hand-written templates — unbracketed, single line.
        [InlineData("DELETE FROM advance_settlements WHERE id=@id AND org_id=@org", "DELETE FROM", "advance_settlements")]
        [InlineData("DELETE FROM contract_payments WHERE id=@id AND org_id=@org", "DELETE FROM", "contract_payments")]
        [InlineData("DELETE FROM org_members WHERE id=@id AND org_id=@org", "DELETE FROM", "org_members")]
        [InlineData("UPDATE organizations SET name=@name WHERE id=@org", "UPDATE", "organizations")]
        [InlineData("UPDATE org_members SET role=@role, status='active' WHERE id=@id AND org_id=@org", "UPDATE", "org_members")]
        [InlineData("UPDATE puja_years SET is_active = 0 WHERE id=@id AND org_id=@org", "UPDATE", "puja_years")]
        // Multi-line raw-string templates (AdminEndpoints/ContractEndpoints/etc.) — leading blank line + indentation.
        [InlineData("\n    INSERT INTO puja_years (id, org_id, year, name, target, projection_pct, is_active)\n    VALUES (@id, @org, @year, @name, @target, @pct, 1)\n    ", "INSERT INTO", "puja_years")]
        [InlineData("\n    INSERT INTO workflow_notes (id, org_id, workflow_item_id, author_id, author_name, note)\n    VALUES (@id, @org, @item, @authorId, @authorName, @note)\n    ", "INSERT INTO", "workflow_notes")]
        // Multi-line UPDATE whose WHERE clause contains a nested subquery with its own WHERE (AdvanceEndpoints settle/reopen).
        [InlineData("\n    UPDATE advances SET status = 'settled'\n     WHERE id = @id AND org_id = @org AND status = 'open'\n       AND (SELECT outstanding FROM v_advance_balances WHERE id = @id AND org_id = @org) <= 0.01\n    ", "UPDATE", "advances")]
        public void Extracts_verb_and_table_from_real_call_site_shapes(string sql, string expectedVerb, string expectedTable)
        {
            var (verb, table) = DynamicSql.ParseVerbAndTable(sql);
            Assert.Equal(expectedVerb, verb);
            Assert.Equal(expectedTable, table);
        }

        [Theory]
        [InlineData("insert into donors (id) values (@id)", "INSERT INTO", "donors")]
        [InlineData("Delete From Donors Where id=@id", "DELETE FROM", "Donors")]
        public void Verb_matching_is_case_insensitive_and_normalised_to_uppercase(string sql, string expectedVerb, string expectedTable)
        {
            var (verb, table) = DynamicSql.ParseVerbAndTable(sql);
            Assert.Equal(expectedVerb, verb);
            Assert.Equal(expectedTable, table);
        }

        [Theory]
        [InlineData("SELECT * FROM expenses WHERE id=@id")]
        [InlineData("")]
        [InlineData("   ")]
        public void Non_mutating_or_unrecognised_statements_yield_no_match(string sql)
        {
            var (verb, table) = DynamicSql.ParseVerbAndTable(sql);
            Assert.Null(verb);
            Assert.Null(table);
        }
    }

    public class PrimaryResource
    {
        [Theory]
        [InlineData("/api/expenses", "expenses")]
        [InlineData("/api/contracts/3f2504e0-4f89-41d3-9a0c-0305e82c3301", "contracts")]
        [InlineData("/api/contract_payments", "contract_payments")]
        [InlineData("/api/subscriptions", "subscriptions")]
        [InlineData("/API/Expenses", "expenses")] // case-insensitive, lower-cased
        public void Extracts_the_resource_segment(string path, string expected)
        {
            Assert.Equal(expected, DynamicSql.PrimaryResource(path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("/")]
        [InlineData("/health")]
        public void Returns_null_for_non_api_paths(string? path)
        {
            Assert.Null(DynamicSql.PrimaryResource(path));
        }

        // The anti-spam guarantee: a contract delete also nulls its expenses'
        // contract_id (both notifiable tables), but only the contracts write —
        // the one the request path names — should notify. The expenses write is
        // recognised as a side-effect because its table != the path's resource.
        [Fact]
        public void Contract_delete_path_names_contracts_not_the_cascaded_expenses()
        {
            Assert.Equal("contracts", DynamicSql.PrimaryResource("/api/contracts/3f2504e0-4f89-41d3-9a0c-0305e82c3301"));
        }
    }

    public class ExtractParameters
    {
        [Fact]
        public void Strips_the_leading_at_sign_and_keeps_values()
        {
            var result = DynamicSql.ExtractParameters([new SqlParameter("@name", "Barrackpore"), new SqlParameter("@amount", 500m)]);

            Assert.Equal("Barrackpore", result["name"]);
            Assert.Equal(500m, result["amount"]);
        }

        [Fact]
        public void Normalises_DBNull_to_null()
        {
            var result = DynamicSql.ExtractParameters([new SqlParameter("@notes", DBNull.Value)]);

            Assert.True(result.ContainsKey("notes"));
            Assert.Null(result["notes"]);
        }

        [Fact]
        public void Empty_parameter_list_yields_empty_dictionary()
        {
            Assert.Empty(DynamicSql.ExtractParameters([]));
        }
    }

    public class BuildEntry
    {
        // The tenant is supplied by the executor, never a default: an audit
        // event filed under the wrong partition would put one committee's
        // trail in another's.
        private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");

        [Fact]
        public void Populates_who_and_where_from_the_ambient_request()
        {
            var userId = Guid.NewGuid();
            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "secretariat@example.org"),
                new Claim(ClaimTypes.Role, "Secretariat"),
            ]));
            httpContext.Request.Method = "PATCH";
            httpContext.Request.Path = "/api/expenses/123";
            httpContext.Request.Headers.UserAgent = "TestAgent/1.0";
            httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");

            var entry = DynamicSql.BuildEntry(
                "expenses", "UPDATE", AuditEntry.Success, errorMessage: null, entityLabel: null,
                before: null, after: new Dictionary<string, object?> { ["amount"] = 500m }, httpContext, Tenant);

            Assert.Equal(userId, entry.UserId);
            Assert.Equal("secretariat@example.org", entry.UserEmail);
            Assert.Equal("Secretariat", entry.UserRole);
            Assert.Equal("PATCH", entry.RequestMethod);
            Assert.Equal("/api/expenses/123", entry.RequestPath);
            Assert.Equal("TestAgent/1.0", entry.UserAgent);
            Assert.Equal("10.0.0.5", entry.IpAddress);
            Assert.Equal(Tenant, entry.OrgId);
        }

        [Fact]
        public void Falls_back_cleanly_with_no_ambient_request()
        {
            var entry = DynamicSql.BuildEntry(
                "expenses", "DELETE FROM", AuditEntry.Success, errorMessage: null, entityLabel: null,
                before: null, after: new Dictionary<string, object?>(), httpContext: null, orgId: Tenant);

            Assert.Null(entry.UserId);
            Assert.Null(entry.UserEmail);
            Assert.Null(entry.UserRole);
            Assert.Null(entry.RequestMethod);
            Assert.Null(entry.RequestPath);
            Assert.Null(entry.IpAddress);
            Assert.Null(entry.UserAgent);
            // Still gets a correlation id even with nothing to correlate to.
            Assert.False(string.IsNullOrEmpty(entry.CorrelationId));
        }

        [Theory]
        [InlineData("DELETE FROM", "DELETE")]
        [InlineData("UPDATE", "UPDATE")]
        [InlineData("INSERT INTO", "INSERT INTO")]
        public void Normalises_delete_from_to_delete_and_passes_other_verbs_through(string verb, string expectedAction)
        {
            var entry = DynamicSql.BuildEntry(
                "expenses", verb, AuditEntry.Success, errorMessage: null, entityLabel: null,
                before: null, after: [], httpContext: null, orgId: Tenant);

            Assert.Equal(expectedAction, entry.Action);
        }

        [Fact]
        public void Carries_outcome_error_and_before_after_through_unchanged()
        {
            var before = new List<Dictionary<string, object?>> { new() { ["amount"] = 400m } };
            var after = new Dictionary<string, object?> { ["amount"] = 500m };

            var entry = DynamicSql.BuildEntry(
                "expenses", "UPDATE", AuditEntry.Failure, "Timeout expired", entityLabel: null,
                before, after, httpContext: null, orgId: Tenant);

            Assert.Equal(AuditEntry.Failure, entry.Outcome);
            Assert.Equal("Timeout expired", entry.ErrorMessage);
            Assert.Same(before, entry.Before);
            Assert.Same(after, entry.After);
        }
    }

    public class DiffChangedFields
    {
        [Fact]
        public void Keeps_only_columns_whose_value_actually_changed()
        {
            var before = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = "1", ["donor_id"] = "d1", ["billed"] = 500m, ["collected"] = 200m, ["mode"] = "Cash" },
            };
            var after = new Dictionary<string, object?>
            {
                ["id"] = "1", ["donor_id"] = "d1", ["billed"] = 500m, ["collected"] = 500m, ["mode"] = "Cash",
            };

            var (resultBefore, resultAfter) = DynamicSql.DiffChangedFields(before, after);

            Assert.NotNull(resultBefore);
            var row = Assert.Single(resultBefore!);
            Assert.Equal(new Dictionary<string, object?> { ["collected"] = 200m }, row);
            Assert.Equal(new Dictionary<string, object?> { ["collected"] = 500m }, resultAfter);
        }

        [Fact]
        public void Treats_typed_before_value_and_stringly_after_value_as_equal_when_they_print_the_same()
        {
            // Before comes back from SqlDataReader typed (decimal); after is
            // whatever the SqlParameter carried off the request — same value,
            // different CLR type, should not be flagged as a change.
            var before = new List<Dictionary<string, object?>> { new() { ["amount"] = 500m } };
            var after = new Dictionary<string, object?> { ["amount"] = 500.00m };

            var (resultBefore, resultAfter) = DynamicSql.DiffChangedFields(before, after);

            Assert.Null(resultBefore);
            Assert.Empty(resultAfter);
        }

        [Fact]
        public void Drops_org_id_from_consideration_even_when_present_in_both()
        {
            var before = new List<Dictionary<string, object?>> { new() { ["org_id"] = "org-a", ["amount"] = 100m } };
            var after = new Dictionary<string, object?> { ["org_id"] = "org-a", ["amount"] = 250m };

            var (resultBefore, resultAfter) = DynamicSql.DiffChangedFields(before, after);

            var row = Assert.Single(resultBefore!);
            Assert.False(row.ContainsKey("org_id"));
            Assert.False(resultAfter.ContainsKey("org_id"));
        }

        [Fact]
        public void A_column_only_present_after_is_kept_with_no_before_row_for_it()
        {
            // "bill_no" has nothing to diff against (didn't exist beforehand,
            // or capture didn't run) — the value should still show up in
            // "after" with no matching "before" entry rather than being
            // silently dropped, since assigning a bill no. for the first
            // time is itself a real change worth recording.
            var before = new List<Dictionary<string, object?>> { new() { ["amount"] = 100m, ["bill_no"] = null } };
            var after = new Dictionary<string, object?> { ["amount"] = 100m, ["bill_no"] = "B-42" };

            var (resultBefore, resultAfter) = DynamicSql.DiffChangedFields(before, after);

            Assert.NotNull(resultBefore);
            var row = Assert.Single(resultBefore!);
            Assert.Equal(new Dictionary<string, object?> { ["bill_no"] = null }, row);
            Assert.Equal("B-42", resultAfter["bill_no"]);
        }

        [Fact]
        public void Null_before_snapshot_is_passed_through_unchanged()
        {
            var after = new Dictionary<string, object?> { ["amount"] = 100m };

            var (resultBefore, resultAfter) = DynamicSql.DiffChangedFields(null, after);

            Assert.Null(resultBefore);
            Assert.Same(after, resultAfter);
        }

        [Fact]
        public void Multi_row_before_snapshot_is_passed_through_unchanged()
        {
            // A bulk update touches more than one row — there's no single
            // "before" to diff a single "after" parameter set against, so the
            // untrimmed snapshot is kept rather than guessing.
            var before = new List<Dictionary<string, object?>>
            {
                new() { ["amount"] = 100m }, new() { ["amount"] = 200m },
            };
            var after = new Dictionary<string, object?> { ["status"] = "cancelled" };

            var (resultBefore, resultAfter) = DynamicSql.DiffChangedFields(before, after);

            Assert.Same(before, resultBefore);
            Assert.Same(after, resultAfter);
        }
    }
}
