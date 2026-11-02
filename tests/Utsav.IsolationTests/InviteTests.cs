using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>A committee's own admins inviting people by one-time code, bound to an email and a role. Run in committee C.</summary>
[Collection(nameof(DatabaseFixture))]
public class InviteTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task An_admin_invites_an_email_and_that_account_joins_with_the_invited_role()
    {
        var email = $"invitee-{Guid.NewGuid():N}@test.local";
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var created = await admin.PostAsJsonAsync("/api/invites", new { email, role = "finance" });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var code = (await JsonAsync(created)).GetProperty("invite_code").GetString()!;

        var (_, token) = await fixture.NewMemberAsync("invitee", [], email: email);
        using var invitee = fixture.ClientFor(token);
        var redeemed = await invitee.PostAsJsonAsync("/api/auth/redeem-invite", new { code });

        Assert.True(redeemed.StatusCode == HttpStatusCode.OK, await redeemed.Content.ReadAsStringAsync());
        await using var identity = fixture.ContextWithNoTenant();
        var member = await identity.OrgMembers.Join(identity.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u.Email })
            .SingleAsync(x => x.Email == email && x.m.OrgId == fixture.TenantC);
        Assert.Equal(("finance", "active"), (member.m.Role, member.m.Status));
    }

    [Fact]
    public async Task The_code_works_only_for_the_invited_address()
    {
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var code = (await JsonAsync(await admin.PostAsJsonAsync("/api/invites",
            new { email = $"right-{Guid.NewGuid():N}@test.local", role = "viewer" }))).GetProperty("invite_code").GetString()!;
        var (_, wrongToken) = await fixture.NewMemberAsync("wrong", []);
        using var wrong = fixture.ClientFor(wrongToken);

        var response = await wrong.PostAsJsonAsync("/api/auth/redeem-invite", new { code });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_secretariat_member_cannot_invite_an_admin_but_can_invite_others()
    {
        var (_, token) = await fixture.NewMemberAsync("sec", [(fixture.TenantC, "secretariat", "active")], fixture.TenantC);
        using var secretariat = fixture.ClientFor(token);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await secretariat.PostAsJsonAsync("/api/invites", new { email = "a@test.local", role = "admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await secretariat.PostAsJsonAsync("/api/invites", new { email = "b@test.local", role = "executive" })).StatusCode);
    }

    [Theory]
    [InlineData("finance")]
    [InlineData("executive")]
    [InlineData("viewer")]
    public async Task Only_those_who_manage_users_can_invite(string role)
    {
        var (_, token) = await fixture.NewMemberAsync("nope", [(fixture.TenantC, role, "active")], fixture.TenantC);
        using var client = fixture.ClientFor(token);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/invites", new { email = "x@test.local", role = "viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/invites")).StatusCode);
    }

    [Fact]
    public async Task Invalid_input_is_refused()
    {
        using var admin = fixture.ClientFor(fixture.TokenForC);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/invites", new { email = "not-an-email", role = "viewer" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/invites", new { email = "ok@test.local", role = "emperor" })).StatusCode);
    }

    [Fact]
    public async Task Outstanding_invites_list_without_codes_and_can_be_revoked_and_stay_in_their_committee()
    {
        var email = $"revoke-{Guid.NewGuid():N}@test.local";
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var created = await JsonAsync(await admin.PostAsJsonAsync("/api/invites", new { email, role = "viewer" }));
        var code = created.GetProperty("invite_code").GetString()!;

        var listed = await admin.GetStringAsync("/api/invites");
        Assert.Contains(email, listed);
        Assert.DoesNotContain(code, listed);
        using var asA = fixture.ClientFor(fixture.TokenForA);
        Assert.DoesNotContain(email, await asA.GetStringAsync("/api/invites"));

        var id = JsonSerializer.Deserialize<JsonElement>(listed).EnumerateArray().First(i => i.GetProperty("email").GetString() == email).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/invites/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await asA.DeleteAsync($"/api/invites/{id}")).StatusCode);

        var (_, token) = await fixture.NewMemberAsync("late", [], email: email);
        using var invitee = fixture.ClientFor(token);
        Assert.NotEqual(HttpStatusCode.OK, (await invitee.PostAsJsonAsync("/api/auth/redeem-invite", new { code })).StatusCode);
    }

    [Fact]
    public async Task A_newer_invite_for_the_same_address_replaces_the_older_one()
    {
        var email = $"twice-{Guid.NewGuid():N}@test.local";
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var first = (await JsonAsync(await admin.PostAsJsonAsync("/api/invites", new { email, role = "viewer" }))).GetProperty("invite_code").GetString()!;
        await admin.PostAsJsonAsync("/api/invites", new { email, role = "viewer" });
        var (_, token) = await fixture.NewMemberAsync("twice", [], email: email);
        using var invitee = fixture.ClientFor(token);

        Assert.NotEqual(HttpStatusCode.OK, (await invitee.PostAsJsonAsync("/api/auth/redeem-invite", new { code = first })).StatusCode);
    }
}

/// <summary>Changing the yearly raise, then re-applying it to every membership ask. Run in committee C.</summary>
[Collection(nameof(DatabaseFixture))]
public class ProjectionRecalculationTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<Guid> BillAsync(HttpClient admin, string category, decimal prior, decimal projected)
    {
        var donor = await JsonAsync(await admin.PostAsJsonAsync("/api/donors", new { name = "Proj " + Guid.NewGuid().ToString("N")[..6] }));
        var bill = await admin.PostAsJsonAsync("/api/subscriptions", new
        {
            donor_id = donor.GetProperty("id").GetGuid(), year_id = fixture.C.Year, category, prior_billed = prior, projected,
        });
        Assert.True(bill.StatusCode == HttpStatusCode.OK, await bill.Content.ReadAsStringAsync());
        return (await JsonAsync(bill)).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Recalculating_applies_the_raise_to_membership_asks_only()
    {
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var membership = await BillAsync(admin, "membership", 1000, 1000);
        var special = await BillAsync(admin, "special", 500, 500);
        var noPrior = await BillAsync(admin, "membership", 0, 321);
        await admin.PatchAsJsonAsync($"/api/puja_years/{fixture.C.Year}", new { projection_pct = 12.5 });

        var response = await admin.PostAsJsonAsync($"/api/puja_years/{fixture.C.Year}/recalculate_projections", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = fixture.ContextFor(fixture.TenantC);
        Assert.Equal(1125m, (await db.Subscriptions.SingleAsync(s => s.Id == membership)).Projected);
        Assert.Equal(500m, (await db.Subscriptions.SingleAsync(s => s.Id == special)).Projected);
        Assert.Equal(321m, (await db.Subscriptions.SingleAsync(s => s.Id == noPrior)).Projected);
    }

    [Fact]
    public async Task Only_an_admin_can_recalculate_and_another_committees_year_is_not_found()
    {
        var (_, token) = await fixture.NewMemberAsync("proj-fin", [(fixture.TenantC, "finance", "active")], fixture.TenantC);
        using var finance = fixture.ClientFor(token);
        using var admin = fixture.ClientFor(fixture.TokenForC);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await finance.PostAsJsonAsync($"/api/puja_years/{fixture.C.Year}/recalculate_projections", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"/api/puja_years/{fixture.A.Year}/recalculate_projections", new { })).StatusCode);
    }
}
