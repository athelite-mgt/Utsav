using Utsav.Domain.Authorization;

namespace Utsav.UnitTests.Authorization;

public class PlatformAdminSeparationTests
{
    [Fact]
    public void Platform_administration_is_not_a_committee_role()
    {
        // If it were a sixth AppRole, approving someone into it at one committee
        // would hand them the whole platform, and a treasurer could end up able to
        // read another committee's books. The two are different kinds of authority.
        Assert.DoesNotContain(AppRoleExtensions.AllWireValues, v => v.Contains("platform", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_committee_permission_is_a_platform_permission()
    {
        // The matrix grants permissions per committee role. None of its
        // permissions may be about operating the platform.
        Assert.DoesNotContain(Enum.GetNames<Permission>(), p => p.Contains("platform", StringComparison.OrdinalIgnoreCase));
    }
}
