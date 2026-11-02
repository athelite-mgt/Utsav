using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Utsav.Api.Authentication;
using Utsav.Api.Endpoints;
using Utsav.Domain.Authorization;

namespace Utsav.UnitTests.Authorization;

/// <summary>
/// Covers the two pieces of the RBAC wiring that aren't exercised by
/// <see cref="PermissionMatrixTests"/>: the hand-typed table-to-permission
/// map the generic CRUD route relies on, and the handler that actually reads
/// the role claim off a request. Both are new surface, both are the kind of
/// thing a typo slips through silently — a table missing from the map fails
/// closed today (see <see cref="CrudEndpoints"/>'s <c>Forbidden</c>), but a
/// table mapped to the wrong permission would fail open.
/// </summary>
public class TableRegistryPermissionTests
{
    [Fact]
    public void Every_registered_table_has_a_required_permission()
    {
        foreach (var table in TableRegistry.Tables.Keys)
        {
            Assert.True(TableRegistry.RequiredPermission.ContainsKey(table), $"{table} is missing from RequiredPermission");
        }
    }

    [Fact]
    public void Required_permission_has_no_entries_for_unknown_tables()
    {
        foreach (var table in TableRegistry.RequiredPermission.Keys)
        {
            Assert.True(TableRegistry.Tables.ContainsKey(table), $"{table} in RequiredPermission is not a registered table");
        }
    }

    [Theory]
    [InlineData("donors", Permission.WriteIncome)]
    [InlineData("subscriptions", Permission.WriteIncome)]
    [InlineData("advertisers", Permission.WriteIncome)]
    [InlineData("ad_bookings", Permission.WriteIncome)]
    [InlineData("special_donations", Permission.WriteIncome)]
    [InlineData("offerings", Permission.WriteIncome)]
    [InlineData("expenses", Permission.WriteFinance)]
    [InlineData("budget_lines", Permission.WriteFinance)]
    [InlineData("cash_ledger", Permission.WriteFinance)]
    [InlineData("fund_transfers", Permission.WriteFinance)]
    [InlineData("vendors", Permission.WriteFinance)]
    [InlineData("puja_schedule", Permission.WriteWorkflow)]
    [InlineData("workflow_items", Permission.WriteWorkflow)]
    [InlineData("lookups", Permission.ManageSettings)]
    [InlineData("zones", Permission.ManageSettings)]
    [InlineData("collectors", Permission.ManageSettings)]
    [InlineData("puja_years", Permission.ManageSettings)]
    public void Table_maps_to_the_expected_permission(string table, Permission expected) =>
        Assert.Equal(expected, TableRegistry.RequiredPermission[table]);

    /// <summary>
    /// An Executive (writes Workflow/Advances, nothing money-shaped) must be
    /// refused on every income and finance table via the generic CRUD route —
    /// this is the actual enforcement point Boss will hit through the UI, not
    /// just the matrix cell in isolation.
    /// </summary>
    [Fact]
    public void Executive_is_refused_on_every_income_and_finance_table()
    {
        foreach (var (table, permission) in TableRegistry.RequiredPermission)
        {
            var allowed = PermissionMatrix.Allows(AppRole.Executive, permission);
            if (permission is Permission.WriteIncome or Permission.WriteFinance or Permission.ManageSettings)
            {
                Assert.False(allowed, $"Executive should not be able to write {table}");
            }
        }
    }

    [Fact]
    public void Executive_can_write_workflow_tables()
    {
        Assert.True(PermissionMatrix.Allows(AppRole.Executive, TableRegistry.RequiredPermission["workflow_items"]));
        Assert.True(PermissionMatrix.Allows(AppRole.Executive, TableRegistry.RequiredPermission["puja_schedule"]));
    }
}

/// <summary>
/// Exercises <see cref="PermissionAuthorizationHandler"/> directly against a
/// <see cref="ClaimsPrincipal"/>, the same shape the ASP.NET Core
/// authorization middleware builds from a request's cookie/session claims.
/// </summary>
public class PermissionAuthorizationHandlerTests
{
    private static readonly PermissionAuthorizationHandler Handler = new();

    private static async Task<bool> SucceedsAsync(string? roleClaim, Permission permission)
    {
        var identity = roleClaim is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.Role, roleClaim)]);
        var user = new ClaimsPrincipal(identity);
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);

        await Handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Viewer_role_claim_fails_a_write_requirement() =>
        Assert.False(await SucceedsAsync("viewer", Permission.WriteWorkflow));

    [Fact]
    public async Task Admin_role_claim_succeeds_every_requirement()
    {
        foreach (var permission in Enum.GetValues<Permission>())
        {
            Assert.True(await SucceedsAsync("admin", permission));
        }
    }

    [Fact]
    public async Task Missing_role_claim_fails_closed() =>
        Assert.False(await SucceedsAsync(null, Permission.Read));

    [Fact]
    public async Task Unrecognised_role_claim_fails_closed() =>
        Assert.False(await SucceedsAsync("superadmin", Permission.Read));

    [Fact]
    public async Task Executive_succeeds_workflow_but_not_finance()
    {
        Assert.True(await SucceedsAsync("executive", Permission.WriteWorkflow));
        Assert.False(await SucceedsAsync("executive", Permission.WriteFinance));
    }
}
