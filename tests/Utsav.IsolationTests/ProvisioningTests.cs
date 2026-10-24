using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data.Entities;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// A platform operator creates a committee and hands its first administrator a
/// one-time invite code. These tests hold both halves to account: that a new
/// committee is born complete and isolated, and that the invite cannot be used
/// by anyone but the person it was meant for.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class ProvisioningTests(DatabaseFixture fixture)
{
    private static readonly string[] PlainFields = ["name", "slug", "city", "festival", "first_year", "first_admin_email"];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<(Guid OrgId, string Slug, string Code)> ProvisionAsync(
        string name, string festival, string adminEmail, string? slug = null, int year = 2026)
    {
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);
        var response = await operatorClient.PostAsJsonAsync("/api/platform/committees", new
        {
            name, slug, city = "Barrackpore", festival, first_year = year, first_admin_email = adminEmail,
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        return (body.GetProperty("id").GetGuid(), body.GetProperty("slug").GetString()!, body.GetProperty("invite_code").GetString()!);
    }

    [Theory]
    [InlineData("TokenForA")]
    [InlineData("TokenForB")]
    [InlineData("TokenUnpinned")]
    [InlineData("TokenOutsider")]
    public async Task Only_a_platform_operator_can_create_a_committee(string tokenName)
    {
        var token = (string)typeof(DatabaseFixture).GetProperty(tokenName)!.GetValue(fixture)!;
        using var client = fixture.ClientFor(token);

        var response = await client.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Should Not Exist", festival = "kali", first_year = 2026, first_admin_email = "x@test.local",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var identity = fixture.ContextWithNoTenant();
        Assert.False(await identity.Organizations.AnyAsync(o => o.Name == "Should Not Exist"));
    }

    [Fact]
    public async Task A_new_committee_is_born_complete_with_its_own_festival_calendar()
    {
        var (orgId, slug, _) = await ProvisionAsync("Barrackpore Kali Puja Seed", "kali", "kali.admin@test.local");

        Assert.Equal("barrackpore-kali-puja-seed", slug);

        await using var db = fixture.ContextFor(orgId);
        var days = await db.Lookups.Where(l => l.Category == "puja_day").OrderBy(l => l.SortOrder).Select(l => l.Label).ToListAsync();
        Assert.Contains("Kali Puja", days);
        Assert.DoesNotContain("Mahalaya", days);

        Assert.NotEmpty(await db.Lookups.Where(l => l.Category == "expense_category").ToListAsync());
        Assert.NotEmpty(await db.Zones.ToListAsync());
        var year = Assert.Single(await db.PujaYears.ToListAsync());
        Assert.Equal(2026, year.Year);
        Assert.Equal("Kali Puja 2026", year.Name);
        Assert.True(year.IsActive);

        // No volunteers, no donors: those are a committee's own people.
        Assert.Empty(await db.Collectors.ToListAsync());
        Assert.Empty(await db.Donors.ToListAsync());
    }

    [Fact]
    public async Task A_new_committee_has_no_members_until_its_invite_is_redeemed()
    {
        var (orgId, _, _) = await ProvisionAsync("Nobody Yet Committee", "durga", "nobody@test.local");

        await using var identity = fixture.ContextWithNoTenant();
        Assert.Empty(await identity.OrgMembers.Where(m => m.OrgId == orgId).ToListAsync());
    }

    [Fact]
    public async Task The_invite_code_is_stored_only_as_a_hash()
    {
        var (orgId, _, code) = await ProvisionAsync("Hash Check Committee", "durga", "hash@test.local");

        await using var identity = fixture.ContextWithNoTenant();
        var invite = await identity.OrgInvites.SingleAsync(i => i.OrgId == orgId);

        Assert.Equal(InviteCodes.Hash(code), invite.CodeHash);
        Assert.NotEqual(code, invite.CodeHash);
        Assert.False(await identity.OrgInvites.AnyAsync(i => i.CodeHash == code));
        Assert.Equal("hash@test.local", invite.Email);
        Assert.Equal("admin", invite.Role);
        Assert.True(invite.ExpiresAt > DateTime.UtcNow.AddDays(1));
        Assert.Null(invite.RedeemedAt);
    }

    [Fact]
    public async Task A_new_committee_is_invisible_to_the_existing_ones_and_they_to_it()
    {
        var (orgId, _, _) = await ProvisionAsync("Isolated Newcomer", "kali", "iso@test.local");

        using var asA = fixture.ClientFor(fixture.TokenForA);
        Assert.DoesNotContain("Bhoot Chaturdashi", await asA.GetStringAsync("/api/lookups"));

        // With the EF filter off, only row-level security stands between A and
        // the newcomer's rows.
        await using var inA = fixture.ContextFor(fixture.TenantA);
        Assert.Empty(await inA.Lookups.IgnoreQueryFilters().Where(l => l.OrgId == orgId).ToListAsync());

        await using var inNew = fixture.ContextFor(orgId);
        Assert.Empty(await inNew.Lookups.IgnoreQueryFilters().Where(l => l.OrgId == fixture.TenantA).ToListAsync());
    }

    [Fact]
    public async Task Provisioning_leaves_existing_committees_untouched()
    {
        await using var before = fixture.ContextFor(fixture.TenantA);
        var donors = await before.Donors.CountAsync();
        var lookups = await before.Lookups.CountAsync();
        var years = await before.PujaYears.CountAsync();

        await ProvisionAsync("Bystander Check", "durga", "bystander@test.local");

        await using var after = fixture.ContextFor(fixture.TenantA);
        Assert.Equal(donors, await after.Donors.CountAsync());
        Assert.Equal(lookups, await after.Lookups.CountAsync());
        Assert.Equal(years, await after.PujaYears.CountAsync());
    }

    [Fact]
    public async Task A_duplicate_slug_is_refused_not_silently_suffixed()
    {
        await ProvisionAsync("Slug Clash One", "durga", "one@test.local", slug: "slug-clash");
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);

        var second = await operatorClient.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Slug Clash Two", slug = "slug-clash", festival = "durga", first_year = 2026, first_admin_email = "two@test.local",
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await using var identity = fixture.ContextWithNoTenant();
        Assert.Equal(1, await identity.Organizations.CountAsync(o => o.Slug == "slug-clash"));
        Assert.False(await identity.Organizations.AnyAsync(o => o.Name == "Slug Clash Two"));
    }

    [Theory]
    [InlineData("", "durga", "a@test.local")]
    [InlineData("Valid Name", "durga", "")]
    [InlineData("Valid Name", "durga", "not-an-email")]
    public async Task Incomplete_requests_are_rejected_and_create_nothing(string name, string festival, string email)
    {
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);

        var response = await operatorClient.PostAsJsonAsync("/api/platform/committees", new
        {
            name, festival, first_year = 2026, first_admin_email = email,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_operator_can_list_committees_and_sees_no_financial_data()
    {
        var (orgId, _, _) = await ProvisionAsync("Listed Committee", "durga", "listed@test.local");
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);

        var body = await operatorClient.GetStringAsync("/api/platform/committees");

        Assert.Contains("Listed Committee", body);
        Assert.Contains(orgId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Durga Committee", body);
        // A directory of committees, never their books.
        Assert.DoesNotContain(DatabaseFixture.MarkerA, body);
        Assert.DoesNotContain("receipt", body);
    }

    [Fact]
    public async Task Creating_a_committee_is_audited_under_it_and_never_records_the_invite_code()
    {
        var (orgId, _, code) = await ProvisionAsync("Audited Committee", "durga", "audited@test.local");

        var entries = fixture.Audit.Entries.Where(e => e.OrgId == orgId).ToList();

        var created = Assert.Single(entries, e => e.EntityType == "organizations");
        Assert.Equal("platform@test.local", created.UserEmail);
        Assert.Equal("Audited Committee", created.EntityLabel);

        // The code is a credential: it must not reach the audit trail in any form.
        Assert.All(entries, e => Assert.DoesNotContain(code, JsonSerializer.Serialize(e), StringComparison.OrdinalIgnoreCase));
    }

    // ---- re-issuing a lost code ----------------------------------------

    private async Task<HttpResponseMessage> ReissueAsync(string token, Guid orgId, string email)
    {
        using var client = fixture.ClientFor(token);
        return await client.PostAsJsonAsync($"/api/platform/committees/{orgId}/reissue-invite", new { email });
    }

    [Fact]
    public async Task A_lost_code_can_be_replaced_while_the_committee_has_no_members_and_the_old_one_dies()
    {
        var (orgId, _, oldCode) = await ProvisionAsync("Lost Code Committee", "durga", "lost@test.local");

        var response = await ReissueAsync(fixture.TokenPlatform, orgId, "lost@test.local");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newCode = (await JsonAsync(response)).GetProperty("invite_code").GetString()!;
        Assert.NotEqual(oldCode, newCode);

        // The lost one must not stay redeemable: it may be sitting in someone's chat history.
        var (_, token) = await fixture.NewMemberAsync("lost", [], email: "lost@test.local");
        using var client = fixture.ClientFor(token);
        var withOld = await client.PostAsJsonAsync("/api/auth/redeem-invite", new { code = oldCode });
        Assert.True(withOld.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"old code still worked: {withOld.StatusCode}");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/redeem-invite", new { code = newCode })).StatusCode);
    }

    [Fact]
    public async Task A_code_can_be_reissued_to_a_different_address_if_the_first_was_wrong()
    {
        var (orgId, _, _) = await ProvisionAsync("Wrong Address Committee", "durga", "typo@test.local");

        var response = await ReissueAsync(fixture.TokenPlatform, orgId, "right@test.local");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var identity = fixture.ContextWithNoTenant();
        var live = await identity.OrgInvites.Where(i => i.OrgId == orgId && i.RedeemedAt == null && i.ExpiresAt > DateTime.UtcNow).ToListAsync();
        Assert.Equal("right@test.local", Assert.Single(live).Email);
    }

    [Fact]
    public async Task An_operator_cannot_reissue_into_a_committee_that_already_has_members()
    {
        // If they could, an operator could mint an admin invite for their own
        // email in any committee and read its books. The capability must grant
        // no access to a committee's data, so a committee with people in it is
        // beyond the operator's reach.
        await using var before = fixture.ContextWithNoTenant();
        var invitesBefore = await before.OrgInvites.CountAsync(i => i.OrgId == fixture.TenantA);

        var response = await ReissueAsync(fixture.TokenPlatform, fixture.TenantA, "platform@test.local");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var after = fixture.ContextWithNoTenant();
        Assert.Equal(invitesBefore, await after.OrgInvites.CountAsync(i => i.OrgId == fixture.TenantA));
    }

    [Theory]
    [InlineData("TokenForA")]
    [InlineData("TokenForC")]
    [InlineData("TokenOutsider")]
    public async Task Only_a_platform_operator_can_reissue(string tokenName)
    {
        var (orgId, _, _) = await ProvisionAsync($"Reissue Guard {tokenName}", "durga", "guard@test.local");
        var token = (string)typeof(DatabaseFixture).GetProperty(tokenName)!.GetValue(fixture)!;

        var response = await ReissueAsync(token, orgId, "guard@test.local");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reissuing_for_a_committee_that_does_not_exist_is_a_404_and_a_bad_address_a_400()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await ReissueAsync(fixture.TokenPlatform, Guid.NewGuid(), "a@test.local")).StatusCode);

        var (orgId, _, _) = await ProvisionAsync("Bad Email Reissue", "durga", "ok@test.local");
        Assert.Equal(HttpStatusCode.BadRequest, (await ReissueAsync(fixture.TokenPlatform, orgId, "not-an-email")).StatusCode);
    }

    [Fact]
    public async Task Reissuing_is_audited_under_that_committee_without_the_code()
    {
        var (orgId, _, _) = await ProvisionAsync("Audited Reissue", "durga", "ar@test.local");
        var response = await ReissueAsync(fixture.TokenPlatform, orgId, "ar@test.local");
        var code = (await JsonAsync(response)).GetProperty("invite_code").GetString()!;

        var entries = fixture.Audit.Entries.Where(e => e.OrgId == orgId && e.EntityType == "org_invites").ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.DoesNotContain(code, JsonSerializer.Serialize(e), StringComparison.OrdinalIgnoreCase));
    }

    // ---- redeeming ------------------------------------------------------

    [Fact]
    public async Task The_invited_person_redeems_the_code_and_lands_in_their_committee_as_admin()
    {
        var (orgId, _, code) = await ProvisionAsync("Redeemed Committee", "kali", "founder@test.local");
        var (userId, token) = await fixture.NewMemberAsync("founder", [], email: "founder@test.local");
        using var client = fixture.ClientFor(token);

        var response = await client.PostAsJsonAsync("/api/auth/redeem-invite", new { code });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var me = await JsonAsync(response);
        Assert.Equal(orgId.ToString(), me.GetProperty("org").GetProperty("id").GetString());
        Assert.Equal("admin", me.GetProperty("role").GetString());

        // And they really are in: the new committee's own data, nobody else's.
        var zones = await client.GetStringAsync("/api/zones");
        Assert.Contains("General", zones);
        Assert.DoesNotContain(DatabaseFixture.MarkerA, zones);
        Assert.DoesNotContain(DatabaseFixture.MarkerB, zones);

        await using var identity = fixture.ContextWithNoTenant();
        var member = await identity.OrgMembers.SingleAsync(m => m.OrgId == orgId && m.UserId == userId);
        Assert.Equal("active", member.Status);
        Assert.Equal("admin", member.Role);
    }

    [Fact]
    public async Task A_code_works_once_only()
    {
        var (_, _, code) = await ProvisionAsync("Single Use Committee", "durga", "single@test.local");
        var (_, first) = await fixture.NewMemberAsync("first", [], email: "single@test.local");
        using var firstClient = fixture.ClientFor(first);
        Assert.Equal(HttpStatusCode.OK, (await firstClient.PostAsJsonAsync("/api/auth/redeem-invite", new { code })).StatusCode);

        // Someone else, later, presenting the same (now spent) code.
        var (_, second) = await fixture.NewMemberAsync("second", [], email: "second.person@test.local");
        using var secondClient = fixture.ClientFor(second);
        var again = await secondClient.PostAsJsonAsync("/api/auth/redeem-invite", new { code });

        Assert.True(again.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Gone,
            $"a used code was accepted again: {again.StatusCode}");
    }

    [Fact]
    public async Task Someone_who_registered_a_different_address_cannot_use_the_code_and_does_not_burn_it()
    {
        var (orgId, _, code) = await ProvisionAsync("Squat Proof Committee", "durga", "real.admin@test.local");

        var (_, strangerToken) = await fixture.NewMemberAsync("stranger", [], email: "stranger@test.local");
        using var stranger = fixture.ClientFor(strangerToken);
        var attempt = await stranger.PostAsJsonAsync("/api/auth/redeem-invite", new { code });

        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
        await using (var identity = fixture.ContextWithNoTenant())
        {
            Assert.Empty(await identity.OrgMembers.Where(m => m.OrgId == orgId).ToListAsync());
        }

        // The wrong person trying did not use it up: the right one still can.
        var (_, realToken) = await fixture.NewMemberAsync("real", [], email: "real.admin@test.local");
        using var real = fixture.ClientFor(realToken);
        Assert.Equal(HttpStatusCode.OK, (await real.PostAsJsonAsync("/api/auth/redeem-invite", new { code })).StatusCode);
    }

    [Fact]
    public async Task The_address_is_compared_without_regard_to_case_and_the_code_without_formatting()
    {
        var (orgId, _, code) = await ProvisionAsync("Casing Committee", "durga", "Mixed.Case@Test.Local");
        var (_, token) = await fixture.NewMemberAsync("mixed", [], email: "mixed.case@test.local");
        using var client = fixture.ClientFor(token);
        var pasted = string.Join(" - ", Enumerable.Range(0, 4).Select(i => code.Substring(i * 8, 8))).ToLowerInvariant();

        var response = await client.PostAsJsonAsync("/api/auth/redeem-invite", new { code = pasted });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var identity = fixture.ContextWithNoTenant();
        Assert.Single(await identity.OrgMembers.Where(m => m.OrgId == orgId).ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-code")]
    [InlineData("00000000000000000000000000000000")]
    public async Task An_unknown_code_is_a_plain_404_with_no_detail(string code)
    {
        var (_, token) = await fixture.NewMemberAsync("guesser", []);
        using var client = fixture.ClientFor(token);

        var response = await client.PostAsJsonAsync("/api/auth/redeem-invite", new { code });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        var (orgId, _, code) = await ProvisionAsync("Expired Committee", "durga", "late@test.local");
        await using (var identity = fixture.ContextWithNoTenant())
        {
            await identity.OrgInvites.Where(i => i.OrgId == orgId)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.ExpiresAt, DateTime.UtcNow.AddDays(-1)));
        }

        var (_, token) = await fixture.NewMemberAsync("late", [], email: "late@test.local");
        using var client = fixture.ClientFor(token);
        var response = await client.PostAsJsonAsync("/api/auth/redeem-invite", new { code });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Gone);
        await using var check = fixture.ContextWithNoTenant();
        Assert.Empty(await check.OrgMembers.Where(m => m.OrgId == orgId).ToListAsync());
    }

    [Fact]
    public async Task Redeeming_requires_a_signed_in_account()
    {
        using var anonymous = fixture.AnonymousClient();

        var response = await anonymous.PostAsJsonAsync("/api/auth/redeem-invite", new { code = "ABCDEF" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Redeeming_does_not_disturb_an_existing_membership_elsewhere()
    {
        var (orgId, _, code) = await ProvisionAsync("Second Home Committee", "durga", "twohats@test.local");
        var (userId, token) = await fixture.NewMemberAsync("twohats", [(fixture.TenantA, "viewer", "active")], pinnedTo: fixture.TenantA, email: "twohats@test.local");
        using var client = fixture.ClientFor(token);

        var response = await client.PostAsJsonAsync("/api/auth/redeem-invite", new { code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var identity = fixture.ContextWithNoTenant();
        var memberships = await identity.OrgMembers.Where(m => m.UserId == userId).ToListAsync();
        Assert.Equal(2, memberships.Count);
        Assert.Equal("viewer", memberships.Single(m => m.OrgId == fixture.TenantA).Role);
        Assert.Equal("admin", memberships.Single(m => m.OrgId == orgId).Role);
    }
}
