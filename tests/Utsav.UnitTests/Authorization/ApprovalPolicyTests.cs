using Utsav.Domain.Authorization;

namespace Utsav.UnitTests.Authorization;

public class ApprovalPolicyTests
{
    [Theory]
    [InlineData(AppRole.Admin, "approved")]
    [InlineData(AppRole.Finance, "approved")]
    [InlineData(AppRole.Secretariat, "pending")]
    [InlineData(AppRole.Executive, "pending")]
    [InlineData(AppRole.Viewer, "pending")]
    public void New_entries_start_approved_only_for_approvers(AppRole role, string expected) =>
        Assert.Equal(expected, ApprovalPolicy.StatusFor(role));

    [Fact]
    public void Only_finance_and_admin_can_approve()
    {
        foreach (var role in Enum.GetValues<AppRole>())
        {
            Assert.Equal(role is AppRole.Finance or AppRole.Admin, ApprovalPolicy.CanApprove(role));
        }
    }

    [Fact]
    public void Only_finance_handles_advances()
    {
        foreach (var role in Enum.GetValues<AppRole>())
        {
            Assert.Equal(role == AppRole.Finance, PermissionMatrix.Allows(role, Permission.WriteAdvances));
        }
    }

    [Theory]
    [InlineData("pending", true)]
    [InlineData("sent_back", true)]
    [InlineData("approved", false)]
    [InlineData("rejected", false)]
    public void Open_statuses_are_the_ones_awaiting_action(string status, bool open) =>
        Assert.Equal(open, ApprovalPolicy.IsOpen(status));
}
