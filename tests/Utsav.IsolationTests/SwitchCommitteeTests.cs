using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// One account, several committees. The session row decides which committee a
/// request runs as, so these tests pin down who may move it and where.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class SwitchCommitteeTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Me_lists_every_committee_the_user_belongs_to_and_the_active_one()
    {
        var (_, token) = await fixture.NewMemberAsync("both", [(fixture.TenantA, "admin", "active"), (fixture.TenantB, "viewer", "active")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);

        var me = await JsonAsync(await client.GetAsync("/api/auth/me"));

        Assert.Equal(2, me.GetProperty("memberships").GetArrayLength());
        Assert.Equal(fixture.TenantA.ToString(), me.GetProperty("org").GetProperty("id").GetString());
        Assert.Equal("admin", me.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Switching_changes_which_committees_data_is_returned()
    {
        var (_, token) = await fixture.NewMemberAsync("both", [(fixture.TenantA, "admin", "active"), (fixture.TenantB, "admin", "active")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);

        var before = await client.GetStringAsync("/api/zones");
        Assert.Contains(fixture.A.Marker, before);
        Assert.DoesNotContain(fixture.B.Marker, before);

        var switched = await client.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantB });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal(fixture.TenantB.ToString(), (await JsonAsync(switched)).GetProperty("org").GetProperty("id").GetString());

        var after = await client.GetStringAsync("/api/zones");
        Assert.Contains(fixture.B.Marker, after);
        Assert.DoesNotContain(fixture.A.Marker, after);
    }

    [Fact]
    public async Task A_role_belongs_to_the_membership_not_the_person()
    {
        // Admin at A, merely a viewer at B. Switching must change what they may do.
        var (_, token) = await fixture.NewMemberAsync("mixed", [(fixture.TenantA, "admin", "active"), (fixture.TenantB, "viewer", "active")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);

        await client.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantB });

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/vendors", new { name = "viewer write" })).StatusCode);
    }

    [Fact]
    public async Task Switching_to_a_committee_the_user_does_not_belong_to_is_refused_and_changes_nothing()
    {
        var (_, token) = await fixture.NewMemberAsync("a-only", [(fixture.TenantA, "admin", "active")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);

        foreach (var target in new[] { fixture.TenantB, Guid.NewGuid() })
        {
            var response = await client.PostAsJsonAsync("/api/auth/switch-org", new { org_id = target });
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var me = await JsonAsync(await client.GetAsync("/api/auth/me"));
        Assert.Equal(fixture.TenantA.ToString(), me.GetProperty("org").GetProperty("id").GetString());
    }

    [Fact]
    public async Task A_pending_membership_is_not_enough_to_switch_in()
    {
        var (_, token) = await fixture.NewMemberAsync("pending-b", [(fixture.TenantA, "admin", "active"), (fixture.TenantB, "viewer", "pending")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);

        var response = await client.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantB });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_user_with_several_committees_and_none_chosen_can_pick_one()
    {
        var (_, token) = await fixture.NewMemberAsync("unpinned", [(fixture.TenantA, "admin", "active"), (fixture.TenantB, "admin", "active")]);
        using var client = fixture.ClientFor(token);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/zones")).StatusCode);

        var switched = await client.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantA });
        Assert.Equal(HttpStatusCode.OK, switched.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/zones")).StatusCode);
    }

    [Fact]
    public async Task A_revoked_membership_stops_working_on_the_very_next_request()
    {
        var (userId, token) = await fixture.NewMemberAsync("revoked", [(fixture.TenantA, "admin", "active")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/zones")).StatusCode);

        await using (var identity = fixture.ContextWithNoTenant())
        {
            await identity.OrgMembers.Where(m => m.UserId == userId).ExecuteDeleteAsync();
        }

        // The session row still names committee A. Membership is re-verified on
        // every request, so it is refused now, not at session expiry.
        var response = await client.GetAsync("/api/zones");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(fixture.A.Marker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Switch_requires_a_signed_in_session_and_an_org_id()
    {
        using var anonymous = fixture.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantA })).StatusCode);

        var (_, token) = await fixture.NewMemberAsync("noid", [(fixture.TenantA, "admin", "active")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/switch-org", new { })).StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_can_ask_to_join_a_committee_by_its_slug()
    {
        // There is no default committee any more, so joining is something the
        // user asks for by name. It creates a PENDING membership: the
        // committee's own admin still has to approve it.
        var (userId, token) = await fixture.NewMemberAsync("joiner", []);
        using var client = fixture.ClientFor(token);

        var response = await client.PostAsJsonAsync("/api/auth/request-access", new { slug = "kali" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Idempotent.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/request-access", new { slug = "kali" })).StatusCode);

        await using var identity = fixture.ContextWithNoTenant();
        var memberships = await identity.OrgMembers.Where(m => m.UserId == userId).ToListAsync();
        var only = Assert.Single(memberships);
        Assert.Equal(fixture.TenantB, only.OrgId);
        Assert.Equal("pending", only.Status);

        // Pending is not access.
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync("/api/zones")).StatusCode);
    }

    [Fact]
    public async Task Asking_to_join_an_unknown_committee_is_a_404_and_creates_nothing()
    {
        var (userId, token) = await fixture.NewMemberAsync("lost", []);
        using var client = fixture.ClientFor(token);

        var response = await client.PostAsJsonAsync("/api/auth/request-access", new { slug = "no-such-committee" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var identity = fixture.ContextWithNoTenant();
        Assert.Empty(await identity.OrgMembers.Where(m => m.UserId == userId).ToListAsync());
    }

    [Fact]
    public async Task Asking_again_never_downgrades_an_existing_active_member()
    {
        var (userId, token) = await fixture.NewMemberAsync("already", [(fixture.TenantA, "admin", "active")], pinnedTo: fixture.TenantA);
        using var client = fixture.ClientFor(token);

        await client.PostAsJsonAsync("/api/auth/request-access", new { slug = "durga" });

        await using var identity = fixture.ContextWithNoTenant();
        var member = await identity.OrgMembers.SingleAsync(m => m.UserId == userId);
        Assert.Equal("active", member.Status);
        Assert.Equal("admin", member.Role);
    }
}
