using Utsav.Api.Asura;
using Utsav.Api.Endpoints;

namespace Utsav.UnitTests.Asura;

public class AsuraCatalogTests
{
    /// <summary>
    /// The test that matters most in this file. Boss's instruction was to
    /// exclude credentials and secrets while allowing the business data; this
    /// asserts that line is actually held, so a future edit that drops
    /// <c>users</c> into the catalogue fails here rather than quietly shipping
    /// password hashes to a language model.
    /// </summary>
    [Theory]
    [InlineData("users")]
    [InlineData("sessions")]
    [InlineData("api_keys")]
    [InlineData("push_subscriptions")]
    [InlineData("org_role_mappings")]
    [InlineData("organizations")]
    [InlineData("notifications")]
    public void Credential_bearing_tables_are_not_readable(string table)
    {
        Assert.False(AsuraCatalog.IsReadable(table));
        // FrozenSet satisfies both ISet<T> and IReadOnlySet<T>, so the
        // Assert.Contains overloads are ambiguous — the cast picks one.
        Assert.Contains(table, (IReadOnlySet<string>)AsuraCatalog.PermanentlyExcluded);
    }

    [Fact]
    public void The_exclusion_list_and_the_allowlist_never_overlap()
    {
        var overlap = AsuraCatalog.Objects
            .Where(AsuraCatalog.PermanentlyExcluded.Contains)
            .ToList();
        Assert.Empty(overlap);
    }

    [Theory]
    [InlineData("donors")]
    [InlineData("subscriptions")]
    [InlineData("expenses")]
    [InlineData("budget_lines")]
    [InlineData("vendors")]
    [InlineData("zones")]
    [InlineData("collectors")]
    [InlineData("puja_years")]
    [InlineData("advances")]
    [InlineData("v_budget_vs_actual")]
    [InlineData("v_advance_balances")]
    [InlineData("v_contract_status")]
    public void The_business_tables_a_report_needs_are_readable(string table)
    {
        Assert.True(AsuraCatalog.IsReadable(table));
    }

    [Fact]
    public void Table_names_are_matched_case_insensitively()
    {
        // SQL Server is case-insensitive about identifiers, so the catalogue
        // must be too — otherwise "SELECT … FROM Donors" bypasses the check
        // by simply not matching the lowercase entry.
        Assert.True(AsuraCatalog.IsReadable("DONORS"));
        Assert.False(AsuraCatalog.IsReadable("USERS"));
    }

    /// <summary>
    /// The read catalogue and the write registry are separate lists with
    /// opposite purposes. This pins that apart: if someone "simplifies" by
    /// pointing one at the other, the counts and contents diverge here first.
    /// </summary>
    [Fact]
    public void The_read_catalogue_is_not_the_write_registry()
    {
        // The write registry has no notion of views; the read catalogue needs them.
        Assert.True(AsuraCatalog.IsReadable("v_budget_vs_actual"));
        Assert.False(TableRegistry.IsKnown("v_budget_vs_actual"));

        // And the read catalogue covers tables the generic CRUD route never writes.
        Assert.True(AsuraCatalog.IsReadable("contracts"));
        Assert.False(TableRegistry.IsKnown("contracts"));
    }

    // ---- column-level redaction --------------------------------------

    [Theory]
    [InlineData("subscriptions", "password_hash")]
    [InlineData("donors", "api_key")]
    [InlineData("vendors", "vapid_private_key")]
    [InlineData("expenses", "refresh_token")]
    public void Secret_looking_columns_are_hidden_even_on_a_catalogued_table(
        string table, string column)
    {
        // Defence in depth: if a migration adds such a column to a table that
        // is already readable, it stays invisible without anyone editing this file.
        Assert.False(AsuraCatalog.IsReadableColumn(table, column));
    }

    [Theory]
    [InlineData("email")]
    [InlineData("user_id")]
    public void Org_member_identity_columns_are_hidden(string column)
    {
        // A reporting assistant needs a member's name and role to label a row.
        // It never needs their email address.
        Assert.False(AsuraCatalog.IsReadableColumn("org_members", column));
    }

    [Theory]
    [InlineData("org_members", "name")]
    [InlineData("org_members", "role")]
    [InlineData("subscriptions", "collected")]
    [InlineData("donors", "contact")]
    public void Ordinary_business_columns_stay_readable(string table, string column)
    {
        Assert.True(AsuraCatalog.IsReadableColumn(table, column));
    }
}
