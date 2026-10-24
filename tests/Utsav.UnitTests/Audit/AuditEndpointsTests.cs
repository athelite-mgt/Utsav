using System.Text.Json;
using Utsav.Api.Endpoints;
using AuditEntry = Utsav.Infrastructure.Audit.AuditEntry;

namespace Utsav.UnitTests.Audit;

/// <summary>
/// Pins the exact bug reported live: History showed every "who" as "System"
/// and the field values were unreadable. Root cause was the frontend reading
/// camelCase (userFullName, entityType) against a wire response shaped by the
/// global snake_case JSON policy (user_full_name, entity_type) — a silent
/// mismatch, since JS destructuring of a missing key just yields undefined
/// rather than an error. <see cref="AuditEndpoints.ToView"/> now builds a
/// dedicated view record with explicit PascalCase names chosen so the
/// snake_case policy produces exactly what History.tsx's AuditRow type reads
/// — these tests serialize through the real policy (mirroring
/// ConfigureHttpJsonOptions in Program.cs) so a future rename anywhere in the
/// chain fails a test instead of quietly reintroducing "System" everywhere.
/// </summary>
public class AuditEndpointsTests
{
    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static AuditEntry Entry(
        string action, string table, string? userFullName, string? userEmail, string? entityLabel,
        List<Dictionary<string, object?>>? before = null, Dictionary<string, object?>? after = null) => new(
        Id: "test-id",
        OrgId: Guid.Empty,
        Timestamp: DateTimeOffset.Parse("2026-09-24T10:00:00Z"),
        UserId: Guid.NewGuid(),
        UserEmail: userEmail,
        UserFullName: userFullName,
        UserRole: "finance",
        Action: action,
        EntityType: table,
        EntityLabel: entityLabel,
        RequestMethod: "PATCH",
        RequestPath: $"/api/{table}/123",
        CorrelationId: "corr-1",
        IpAddress: "10.0.0.5",
        UserAgent: "TestAgent/1.0",
        Outcome: AuditEntry.Success,
        ErrorMessage: null,
        Before: before,
        After: after ?? new Dictionary<string, object?>());

    [Fact]
    public void Who_prefers_full_name_over_email()
    {
        var view = AuditEndpoints.ToView(Entry("UPDATE", "expenses", "Ramesh Gupta", "ramesh@example.org", "Decoration"));

        Assert.Equal("Ramesh Gupta", view.Who);
    }

    [Fact]
    public void Who_falls_back_to_email_when_no_full_name()
    {
        var view = AuditEndpoints.ToView(Entry("UPDATE", "expenses", null, "ramesh@example.org", "Decoration"));

        Assert.Equal("ramesh@example.org", view.Who);
    }

    [Fact]
    public void Who_is_System_only_when_neither_name_nor_email_exists()
    {
        var view = AuditEndpoints.ToView(Entry("UPDATE", "expenses", null, null, "Decoration"));

        Assert.Equal("System", view.Who);
    }

    [Fact]
    public void Wire_shape_uses_who_and_page_not_userFullName_and_entityType()
    {
        var view = AuditEndpoints.ToView(Entry("UPDATE", "subscriptions", "Ramesh Gupta", "ramesh@example.org", "Ramesh Gupta"));

        var wire = JsonSerializer.Serialize(view, ApiJson);

        // What History.tsx's AuditRow actually reads.
        Assert.Contains("\"who\":\"Ramesh Gupta\"", wire);
        Assert.Contains("\"page\":\"Membership / Special Donation\"", wire);
        Assert.Contains("\"record\":\"Ramesh Gupta\"", wire);
        // What broke it: the raw entry's own property names must not leak through.
        Assert.DoesNotContain("userFullName", wire);
        Assert.DoesNotContain("entityType", wire);
        Assert.DoesNotContain("user_full_name", wire);
        Assert.DoesNotContain("entity_type", wire);
    }

    [Theory]
    [InlineData("INSERT INTO", "Created")]
    [InlineData("UPDATE", "Updated")]
    [InlineData("DELETE", "Deleted")]
    public void Action_is_the_word_a_reader_expects_not_the_sql_verb(string sqlVerb, string expected)
    {
        var view = AuditEndpoints.ToView(Entry(sqlVerb, "expenses", "Ramesh", "r@example.org", "Decoration"));

        Assert.Equal(expected, view.Action);
    }

    [Fact]
    public void Page_falls_back_to_the_raw_table_name_when_unmapped()
    {
        var view = AuditEndpoints.ToView(Entry("UPDATE", "some_future_table", "Ramesh", "r@example.org", null));

        Assert.Equal("some_future_table", view.Page);
    }

    [Fact]
    public void Settlements_page_as_Collections()
    {
        var view = AuditEndpoints.ToView(Entry("INSERT INTO", "collection_payments", "Ramesh", "r@example.org", "Subir Chatterjee"));

        Assert.Equal("Collections", view.Page);
    }

    [Fact]
    public void Record_is_null_when_the_label_could_not_be_resolved()
    {
        var view = AuditEndpoints.ToView(Entry("UPDATE", "expenses", "Ramesh", "r@example.org", entityLabel: null));

        Assert.Null(view.Record);
    }

    public class BuildChanges
    {
        [Fact]
        public void Reports_field_old_and_new_values()
        {
            // Storage already trims to changed-only fields (DynamicSql.DiffChangedFields)
            // before an entry ever reaches Cosmos, so a real "before" here never
            // carries an unchanged column alongside the changed one.
            var before = new List<Dictionary<string, object?>> { new() { ["collected"] = 200m } };
            var after = new Dictionary<string, object?> { ["collected"] = 500m };
            var entry = Entry("UPDATE", "subscriptions", "Ramesh", "r@example.org", "Ramesh Gupta", before, after);

            var changes = AuditEndpoints.BuildChanges(entry);

            var collected = Assert.Single(changes);
            Assert.Equal("collected", collected.Field);
            Assert.Equal(200m, collected.From);
            Assert.Equal(500m, collected.To);
        }

        [Theory]
        [InlineData("org_id")]
        [InlineData("year_id")]
        [InlineData("id")]
        [InlineData("created_at")]
        [InlineData("updated_at")]
        [InlineData("donor_id")]
        public void Hides_plumbing_columns_a_reader_cannot_interpret(string hiddenColumn)
        {
            var before = new List<Dictionary<string, object?>> { new() { [hiddenColumn] = "old", ["details"] = "old detail" } };
            var after = new Dictionary<string, object?> { [hiddenColumn] = "new", ["details"] = "new detail" };
            var entry = Entry("UPDATE", "expenses", "Ramesh", "r@example.org", "old detail", before, after);

            var changes = AuditEndpoints.BuildChanges(entry);

            Assert.DoesNotContain(changes, c => c.Field == hiddenColumn);
            Assert.Contains(changes, c => c.Field == "details");
        }

        [Theory]
        [InlineData("vendor_id")]
        [InlineData("zone_id")]
        [InlineData("collector_id")]
        public void Shows_fk_columns_DynamicSql_already_resolved_to_a_friendly_name(string fkColumn)
        {
            // These arrive here already resolved from GUID to name (see
            // DynamicSql.ResolveFriendlyFkValuesAsync) — BuildChanges just
            // needs to not hide them, unlike the pure-plumbing columns above.
            var before = new List<Dictionary<string, object?>> { new() { [fkColumn] = "North Zone", ["details"] = "old detail" } };
            var after = new Dictionary<string, object?> { [fkColumn] = "South Zone", ["details"] = "new detail" };
            var entry = Entry("UPDATE", "expenses", "Ramesh", "r@example.org", "old detail", before, after);

            var changes = AuditEndpoints.BuildChanges(entry);

            Assert.Contains(changes, c => c.Field == fkColumn);
        }

        [Fact]
        public void Empty_when_after_is_null()
        {
            var entry = Entry("UPDATE", "expenses", "Ramesh", "r@example.org", "Decoration", before: null, after: null);
            // Entry() defaults After to an empty dict, not null — construct directly to hit the null path.
            var withNullAfter = entry with { After = null };

            Assert.Empty(AuditEndpoints.BuildChanges(withNullAfter));
        }

        [Fact]
        public void Create_entries_show_their_initial_field_values()
        {
            var after = new Dictionary<string, object?> { ["category"] = "Decoration", ["amount"] = 1500m };
            var entry = Entry("INSERT INTO", "expenses", "Ramesh", "r@example.org", "Decoration", before: null, after: after);

            var created = AuditEndpoints.ToView(entry);

            Assert.Contains(created.Changes, c => c.Field == "category" && c.From == null && Equals(c.To, "Decoration"));
            Assert.Contains(created.Changes, c => c.Field == "amount" && c.From == null && Equals(c.To, 1500m));
        }

        [Fact]
        public void Delete_entries_show_what_was_removed()
        {
            var before = new List<Dictionary<string, object?>> { new() { ["category"] = "Decoration", ["amount"] = 1500m } };
            var entry = Entry("DELETE", "expenses", "Ramesh", "r@example.org", "Decoration", before, after: null) with { After = null };

            var deleted = AuditEndpoints.ToView(entry);

            Assert.Contains(deleted.Changes, c => c.Field == "category" && Equals(c.From, "Decoration") && c.To == null);
            Assert.Contains(deleted.Changes, c => c.Field == "amount" && Equals(c.From, 1500m) && c.To == null);
        }

        [Fact]
        public void Still_empty_when_create_or_delete_has_no_captured_fields()
        {
            // The pre-existing fallback: INSERT/DELETE with no before and an
            // empty after (e.g. Entry()'s own defaults) has nothing to show.
            var created = AuditEndpoints.ToView(Entry("INSERT INTO", "expenses", "Ramesh", "r@example.org", "New expense"));
            var deleted = AuditEndpoints.ToView(Entry("DELETE", "expenses", "Ramesh", "r@example.org", "Old expense") with { After = null });

            Assert.Empty(created.Changes);
            Assert.Empty(deleted.Changes);
        }
    }
}
