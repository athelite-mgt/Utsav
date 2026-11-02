using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Operating the platform is a different kind of authority from operating a
/// committee. A committee admin is the most powerful committee role and must
/// still be unable to reach anything platform-level, and a platform operator
/// must gain no access to any committee's books from that capability alone.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class PlatformAdminTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task A_platform_operator_can_reach_the_platform_endpoints()
    {
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);

        var response = await operatorClient.GetAsync("/api/platform/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("TokenForA")]   // admin of committee A
    [InlineData("TokenForB")]   // admin of committee B
    [InlineData("TokenForC")]   // admin of committee C
    [InlineData("TokenUnpinned")]
    [InlineData("TokenOutsider")]
    public async Task No_committee_role_however_senior_reaches_the_platform_endpoints(string tokenName)
    {
        var token = (string)typeof(DatabaseFixture).GetProperty(tokenName)!.GetValue(fixture)!;
        using var client = fixture.ClientFor(token);

        var response = await client.GetAsync("/api/platform/whoami");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_anonymous_caller_is_unauthorised()
    {
        using var anonymous = fixture.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/platform/whoami")).StatusCode);
    }

    [Fact]
    public async Task The_capability_grants_no_access_to_any_committees_data()
    {
        // The operator belongs to no committee, so the platform capability is all
        // they have. It must not unlock a single committee's records.
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);

        foreach (var route in new[] { "/api/zones", "/api/vendors", "/api/users", "/api/documents", "/api/lookups" })
        {
            var response = await operatorClient.GetAsync(route);
            var body = await response.Content.ReadAsStringAsync();

            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(fixture.A.Marker, body);
            Assert.DoesNotContain(fixture.B.Marker, body);
        }
    }

    [Fact]
    public async Task Switching_into_a_committee_is_still_membership_gated_for_an_operator()
    {
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);

        var response = await operatorClient.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantA });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_tells_the_client_whether_the_account_is_a_platform_operator()
    {
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);
        using var memberClient = fixture.ClientFor(fixture.TokenForA);

        var operatorMe = await operatorClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        var memberMe = await memberClient.GetFromJsonAsync<JsonElement>("/api/auth/me");

        Assert.True(operatorMe.GetProperty("is_platform_admin").GetBoolean());
        Assert.False(memberMe.GetProperty("is_platform_admin").GetBoolean());
    }
}

/// <summary>
/// A committee with nobody inside has no one to approve a join request. The
/// operator may seat its first administrator, but only then — never in one that
/// is already running.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class PlatformFirstAdminTests(DatabaseFixture fixture)
{
    private async Task<Guid> EmptyCommitteeAsync()
    {
        using var op = fixture.ClientFor(fixture.TokenPlatform);
        var r = await op.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Empty Puja", slug = "empty-" + Guid.NewGuid().ToString("N")[..8], city = "Barrackpore", festival = "durga", first_year = 2026,
            first_admin_email = $"first-{Guid.NewGuid():N}@test.local",
        });
        Assert.True(r.StatusCode == HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task The_operator_can_approve_a_request_into_a_committee_with_no_members_as_its_admin()
    {
        var org = await EmptyCommitteeAsync();
        var (userId, _) = await fixture.NewMemberAsync("requester", [(org, "viewer", "pending")]);
        await using var identity = fixture.ContextWithNoTenant();
        var memberId = (await identity.OrgMembers.SingleAsync(m => m.OrgId == org && m.UserId == userId)).Id;
        using var op = fixture.ClientFor(fixture.TokenPlatform);

        var listed = await op.GetFromJsonAsync<JsonElement>($"/api/platform/committees/{org}/requests");
        Assert.Equal(memberId, listed[0].GetProperty("member_id").GetGuid());

        var response = await op.PostAsJsonAsync($"/api/platform/committees/{org}/requests/{memberId}/approve", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var after = fixture.ContextWithNoTenant();
        var row = await after.OrgMembers.SingleAsync(m => m.Id == memberId);
        Assert.Equal(("active", "admin"), (row.Status, row.Role));
    }

    [Fact]
    public async Task The_operator_cannot_approve_into_a_committee_that_already_has_members()
    {
        var (userId, _) = await fixture.NewMemberAsync("sneaky", [(fixture.TenantA, "viewer", "pending")]);
        await using var identity = fixture.ContextWithNoTenant();
        var memberId = (await identity.OrgMembers.SingleAsync(m => m.OrgId == fixture.TenantA && m.UserId == userId)).Id;
        using var op = fixture.ClientFor(fixture.TokenPlatform);

        Assert.Equal(HttpStatusCode.Conflict,
            (await op.PostAsJsonAsync($"/api/platform/committees/{fixture.TenantA}/requests/{memberId}/approve", new { })).StatusCode);
        Assert.Equal("[]", (await op.GetStringAsync($"/api/platform/committees/{fixture.TenantA}/requests")).Trim());
        await using var after = fixture.ContextWithNoTenant();
        Assert.Equal("pending", (await after.OrgMembers.SingleAsync(m => m.Id == memberId)).Status);
    }

    [Fact]
    public async Task Only_an_operator_can_use_it()
    {
        var org = await EmptyCommitteeAsync();
        using var asA = fixture.ClientFor(fixture.TokenForA);

        Assert.Equal(HttpStatusCode.Forbidden, (await asA.PostAsJsonAsync($"/api/platform/committees/{org}/requests/{Guid.NewGuid()}/approve", new { })).StatusCode);
    }
}
