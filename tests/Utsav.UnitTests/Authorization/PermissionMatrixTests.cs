using Utsav.Domain.Authorization;

namespace Utsav.UnitTests.Authorization;

/// <summary>
/// The matrix is the whole authorization model, so it is asserted
/// exhaustively — every role against every permission — rather than by
/// spot-checking. A cell changing without this file changing is a bug.
/// </summary>
public class PermissionMatrixTests
{
    // Expected grants, written out independently of the implementation so a
    // typo in PermissionMatrix can't be mirrored by a typo here.
    public static TheoryData<AppRole, Permission, bool> Cells()
    {
        var expected = new Dictionary<AppRole, Permission[]>
        {
            [AppRole.Admin] =
            [
                Permission.Read, Permission.ManageSettings, Permission.ManageUsers,
                Permission.WriteIncome, Permission.WriteFinance,
                Permission.WriteWorkflow, Permission.WriteAdvances, Permission.ReadAudit,
                Permission.UseAsura,
            ],
            [AppRole.Secretariat] =
            [
                Permission.Read, Permission.ManageUsers,
                Permission.WriteIncome, Permission.WriteFinance,
                Permission.WriteWorkflow, Permission.WriteAdvances, Permission.ReadAudit,
                Permission.UseAsura,
            ],
            [AppRole.Finance] =
            [
                Permission.Read, Permission.WriteIncome, Permission.WriteFinance,
                Permission.WriteWorkflow, Permission.WriteAdvances,
                Permission.UseAsura,
            ],
            [AppRole.Executive] =
            [
                Permission.Read, Permission.WriteWorkflow, Permission.WriteAdvances,
            ],
            [AppRole.Viewer] =
            [
                Permission.Read,
            ],
        };

        var data = new TheoryData<AppRole, Permission, bool>();
        foreach (var role in Enum.GetValues<AppRole>())
        {
            foreach (var permission in Enum.GetValues<Permission>())
            {
                data.Add(role, permission, expected[role].Contains(permission));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Allows_matches_the_documented_matrix(AppRole role, Permission permission, bool expected) =>
        Assert.Equal(expected, PermissionMatrix.Allows(role, permission));

    [Fact]
    public void Every_role_can_read()
    {
        foreach (var role in Enum.GetValues<AppRole>())
        {
            Assert.True(PermissionMatrix.Allows(role, Permission.Read), $"{role} should be able to read");
        }
    }

    [Fact]
    public void Only_admin_manages_settings()
    {
        foreach (var role in Enum.GetValues<AppRole>())
        {
            Assert.Equal(role == AppRole.Admin, PermissionMatrix.Allows(role, Permission.ManageSettings));
        }
    }

    [Fact]
    public void Viewer_can_do_nothing_but_read() =>
        Assert.Equal([Permission.Read], PermissionMatrix.GrantedTo(AppRole.Viewer).Order().ToArray());

    /// <summary>
    /// The brief's rule: everyone through Executive writes workflow and advances.
    /// </summary>
    [Theory]
    [InlineData(AppRole.Admin)]
    [InlineData(AppRole.Secretariat)]
    [InlineData(AppRole.Finance)]
    [InlineData(AppRole.Executive)]
    public void Roles_through_executive_write_workflow_and_advances(AppRole role)
    {
        Assert.True(PermissionMatrix.Allows(role, Permission.WriteWorkflow));
        Assert.True(PermissionMatrix.Allows(role, Permission.WriteAdvances));
    }

    [Fact]
    public void Viewer_cannot_write_workflow_or_advances()
    {
        Assert.False(PermissionMatrix.Allows(AppRole.Viewer, Permission.WriteWorkflow));
        Assert.False(PermissionMatrix.Allows(AppRole.Viewer, Permission.WriteAdvances));
    }

    [Fact]
    public void Executive_cannot_touch_the_money()
    {
        Assert.False(PermissionMatrix.Allows(AppRole.Executive, Permission.WriteIncome));
        Assert.False(PermissionMatrix.Allows(AppRole.Executive, Permission.WriteFinance));
    }
}

public class AppRoleTests
{
    [Theory]
    [InlineData(AppRole.Admin, "admin")]
    [InlineData(AppRole.Secretariat, "secretariat")]
    [InlineData(AppRole.Finance, "finance")]
    [InlineData(AppRole.Executive, "executive")]
    [InlineData(AppRole.Viewer, "viewer")]
    public void Round_trips_through_its_wire_value(AppRole role, string wire)
    {
        Assert.Equal(wire, role.ToWire());
        Assert.Equal(role, AppRoleExtensions.ParseWire(wire));
    }

    /// <summary>
    /// The only two values the production database currently holds must keep
    /// their exact meaning across the widening — see TASKLIST R4.
    /// </summary>
    [Fact]
    public void Existing_production_values_still_parse()
    {
        Assert.Equal(AppRole.Admin, AppRoleExtensions.ParseWire("admin"));
        Assert.Equal(AppRole.Viewer, AppRoleExtensions.ParseWire("viewer"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("collector")]   // a role that existed before the collapse to two
    [InlineData("treasurer")]   // ditto
    [InlineData("superadmin")]
    public void Unknown_roles_fail_rather_than_defaulting(string? value)
    {
        Assert.False(AppRoleExtensions.TryParseWire(value, out _));
        Assert.Throws<ArgumentException>(() => AppRoleExtensions.ParseWire(value));
    }

    [Fact]
    public void Wire_values_are_lowercase_and_unique()
    {
        var values = AppRoleExtensions.AllWireValues;
        Assert.Equal(Enum.GetValues<AppRole>().Length, values.Distinct().Count());
        Assert.All(values, v => Assert.Equal(v.ToLowerInvariant(), v));
    }
}
