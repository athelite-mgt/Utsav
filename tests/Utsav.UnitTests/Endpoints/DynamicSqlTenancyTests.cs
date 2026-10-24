using Utsav.Api.Endpoints;
using Xunit;

namespace Utsav.UnitTests.Endpoints;

public class DynamicSqlTenancyTests
{
    [Fact]
    public void Insert_into_an_org_scoped_table_must_name_org_id()
    {
        // TableRegistry deliberately excludes org_id from every column
        // whitelist, so the value can only come from the executor. An INSERT
        // that does not name the column would create an unowned row.
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped(
                "INSERT INTO [donors] ([id],[name]) VALUES (@id,@name)", ["@id", "@name", "@org"]));
    }

    [Fact]
    public void Insert_naming_org_id_with_the_bound_parameter_is_allowed()
    {
        TenancyGuard.AssertScoped(
            "INSERT INTO [donors] ([id],[org_id],[name]) VALUES (@id,@org,@name)",
            ["@id", "@org", "@name"]);
    }

    [Fact]
    public void Update_without_an_org_predicate_is_refused()
    {
        // Without the predicate this would update a matching id in ANY
        // committee: the single most dangerous shape in the codebase.
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("UPDATE [expenses] SET amount=@amount WHERE id=@id", ["@amount", "@id", "@org"]));
    }

    [Fact]
    public void Delete_without_an_org_predicate_is_refused()
    {
        Assert.Throws<TenancyViolationException>(() =>
            TenancyGuard.AssertScoped("DELETE FROM org_members WHERE id=@id", ["@id", "@org"]));
    }

    [Fact]
    public void Delete_with_an_org_predicate_is_allowed()
    {
        TenancyGuard.AssertScoped("DELETE FROM org_members WHERE id=@id AND org_id=@org", ["@id", "@org"]);
    }
}
