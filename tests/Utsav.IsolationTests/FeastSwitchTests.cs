using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Feast can be switched off per committee, by the Admin only. Off means the
/// section stops answering; it never means the data is deleted. Run in
/// committee C, restoring the switch afterwards so test order cannot matter.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class FeastSwitchTests(DatabaseFixture fixture)
{
    private async Task SetAsync(bool on)
    {
        using var admin = fixture.ClientFor(fixture.TokenForC);
        var response = await admin.PatchAsJsonAsync("/api/organizations/current", new { feast_enabled = on });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Admin_can_switch_feast_off_and_on_and_the_session_reports_it()
    {
        try
        {
            await SetAsync(false);
            using var asC = fixture.ClientFor(fixture.TokenForC);
            var me = await asC.GetFromJsonAsync<JsonElement>("/api/auth/me");
            Assert.False(me.GetProperty("org").GetProperty("feast_enabled").GetBoolean());

            await SetAsync(true);
            me = await asC.GetFromJsonAsync<JsonElement>("/api/auth/me");
            Assert.True(me.GetProperty("org").GetProperty("feast_enabled").GetBoolean());
        }
        finally
        {
            await SetAsync(true);
        }
    }

    [Fact]
    public async Task Only_the_admin_can_flip_the_switch()
    {
        var (_, finance) = await fixture.NewMemberAsync("feast-fin", [(fixture.TenantC, "finance", "active")], fixture.TenantC);
        using var client = fixture.ClientFor(finance);

        var response = await client.PatchAsJsonAsync("/api/organizations/current", new { feast_enabled = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task While_off_feast_routes_stop_answering_and_nothing_is_deleted()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        var created = await asC.PostAsJsonAsync("/api/feasts", new { donor_id = fixture.C.Donor, year_id = fixture.C.Year, billed = 100 });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var feastId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        try
        {
            await SetAsync(false);

            Assert.Equal(HttpStatusCode.NotFound, (await asC.GetAsync($"/api/feasts?year_id={fixture.C.Year}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await asC.GetAsync($"/api/feast_expenses?year_id={fixture.C.Year}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await asC.GetAsync($"/api/feast_tokens?year_id={fixture.C.Year}")).StatusCode);

            var write = await asC.PostAsJsonAsync("/api/feasts", new { donor_id = fixture.C.Donor, year_id = fixture.C.Year, billed = 5 });
            Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await asC.PatchAsJsonAsync($"/api/feasts/{feastId}", new { billed = 6 })).StatusCode);

            await using var inC = fixture.ContextFor(fixture.TenantC);
            Assert.True(await inC.Feasts.AnyAsync(f => f.Id == feastId));

            await SetAsync(true);
            Assert.Equal(HttpStatusCode.OK, (await asC.GetAsync($"/api/feasts?year_id={fixture.C.Year}")).StatusCode);
        }
        finally
        {
            await SetAsync(true);
        }
    }

    [Fact]
    public async Task A_newly_provisioned_committee_starts_with_feast_off()
    {
        using var operatorClient = fixture.ClientFor(fixture.TokenPlatform);
        var response = await operatorClient.PostAsJsonAsync("/api/platform/committees", new
        {
            name = "Feast Default Puja", city = "Barrackpore", festival = "durga", first_year = 2026,
            first_admin_email = $"feast-{Guid.NewGuid():N}@test.local",
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var identity = fixture.ContextWithNoTenant();
        Assert.False((await identity.Organizations.SingleAsync(o => o.Id == id)).FeastEnabled);
    }
}
