using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// A committee's name, colour and logo belong to that committee alone, are
/// changed only by its own admin, and never become a way to run anything in a
/// member's browser. All changes happen in committee C.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class BrandingTests(DatabaseFixture fixture)
{
    private static readonly byte[] TinyPng =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1];

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static MultipartFormDataContent Upload(byte[] bytes, string fileName = "logo.png", string contentType = "image/png")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private async Task<string> ViewerTokenInCAsync() =>
        (await fixture.NewMemberAsync("viewer", [(fixture.TenantC, "viewer", "active")], pinnedTo: fixture.TenantC)).Token;

    // ---- colour ---------------------------------------------------------

    [Fact]
    public async Task A_committee_admin_can_set_and_then_clear_the_brand_colour()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var set = await asC.PatchAsJsonAsync("/api/organizations/current", new { accent_colour = "#B45309" });
        Assert.True(set.StatusCode == HttpStatusCode.OK, await set.Content.ReadAsStringAsync());

        var me = await JsonAsync(await asC.GetAsync("/api/auth/me"));
        Assert.Equal("#b45309", me.GetProperty("org").GetProperty("accent_colour").GetString());
        Assert.Equal("#b45309", me.GetProperty("memberships").EnumerateArray()
            .Single(m => m.GetProperty("org_id").GetGuid() == fixture.TenantC).GetProperty("accent_colour").GetString());

        var cleared = await asC.PatchAsJsonAsync("/api/organizations/current", new { accent_colour = (string?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var after = await JsonAsync(await asC.GetAsync("/api/auth/me"));
        Assert.Equal(JsonValueKind.Null, after.GetProperty("org").GetProperty("accent_colour").ValueKind);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#fff;} body{display:none")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("#12")]
    [InlineData("")]
    public async Task A_colour_that_could_carry_css_is_refused_and_nothing_is_stored(string value)
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        await asC.PatchAsJsonAsync("/api/organizations/current", new { accent_colour = "#112233" });

        var response = await asC.PatchAsJsonAsync("/api/organizations/current", new { accent_colour = value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var identity = fixture.ContextWithNoTenant();
        Assert.Equal("#112233", (await identity.Organizations.SingleAsync(o => o.Id == fixture.TenantC)).AccentColour);
    }

    [Fact]
    public async Task Changing_the_name_still_works_and_needs_no_colour()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var response = await asC.PatchAsJsonAsync("/api/organizations/current", new { name = "Flow Committee" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await asC.PatchAsJsonAsync("/api/organizations/current", new { })).StatusCode);
    }

    [Fact]
    public async Task Only_a_settings_admin_can_change_branding_and_only_their_own()
    {
        using var viewer = fixture.ClientFor(await ViewerTokenInCAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PatchAsJsonAsync("/api/organizations/current", new { accent_colour = "#000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/organizations/current/logo", Upload(TinyPng))).StatusCode);

        // C's admin changes C, and the other committees are not touched.
        await using var identity = fixture.ContextWithNoTenant();
        var before = await identity.Organizations.Where(o => o.Id == fixture.TenantA || o.Id == fixture.TenantB)
            .Select(o => new { o.Id, o.AccentColour, o.Name }).ToListAsync();
        using var asC = fixture.ClientFor(fixture.TokenForC);
        await asC.PatchAsJsonAsync("/api/organizations/current", new { accent_colour = "#abcdef" });
        var after = await identity.Organizations.AsNoTracking().Where(o => o.Id == fixture.TenantA || o.Id == fixture.TenantB)
            .Select(o => new { o.Id, o.AccentColour, o.Name }).ToListAsync();
        Assert.Equal(before.OrderBy(x => x.Id), after.OrderBy(x => x.Id));
    }

    [Fact]
    public async Task A_branding_change_is_audited_under_the_committee_that_made_it()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        await asC.PatchAsJsonAsync("/api/organizations/current", new { accent_colour = "#0a0b0c" });

        // Provisioning elsewhere also writes organizations entries (under the NEW
        // committees), so pick out this change by its distinctive value.
        var entries = fixture.Audit.Entries
            .Where(e => e.EntityType == "organizations" && e.After is not null
                        && e.After.Values.Any(v => Equals(v, "#0a0b0c")))
            .ToList();

        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal(fixture.TenantC, e.OrgId));
    }

    // ---- logo -----------------------------------------------------------

    [Fact]
    public async Task A_genuine_image_becomes_the_committees_public_logo_stored_under_its_own_prefix()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var response = await asC.PostAsync("/api/organizations/current/logo", Upload(TinyPng));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var logoUrl = (await JsonAsync(response)).GetProperty("logo_url").GetString()!;

        Assert.StartsWith($"/api/branding/{fixture.TenantC}/logo", logoUrl);
        Assert.Contains(fixture.Blobs.Names, n => n.StartsWith($"{fixture.TenantC}/branding/", StringComparison.Ordinal) && n.EndsWith(".png", StringComparison.Ordinal));

        // The session payload carries it, so the switcher and chooser can show it.
        var me = await JsonAsync(await asC.GetAsync("/api/auth/me"));
        Assert.Equal(logoUrl, me.GetProperty("org").GetProperty("logo_url").GetString());

        // A plain <img> tag cannot send a bearer token, so the logo is public.
        using var anonymous = fixture.AnonymousClient();
        var served = await anonymous.GetAsync(logoUrl);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", served.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(TinyPng, await served.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' onload='alert(1)'><script>alert(1)</script></svg>", "logo.svg", "image/svg+xml")]
    [InlineData("<!DOCTYPE html><script>alert(1)</script>", "logo.png", "image/png")]
    [InlineData("just some text", "logo.png", "image/png")]
    [InlineData("MZ this is an executable", "logo.jpg", "image/jpeg")]
    public async Task Anything_that_is_not_really_a_raster_image_is_refused_whatever_it_claims_to_be(string content, string name, string claimed)
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        var blobsBefore = fixture.Blobs.Names.Count;

        var response = await asC.PostAsync("/api/organizations/current/logo", Upload(Encoding.UTF8.GetBytes(content), name, claimed));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(blobsBefore, fixture.Blobs.Names.Count);
    }

    [Fact]
    public async Task An_oversized_logo_is_refused()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        var huge = TinyPng.Concat(new byte[600 * 1024]).ToArray();

        var response = await asC.PostAsync("/api/organizations/current/logo", Upload(huge));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_file_name_cannot_steer_where_the_logo_is_stored()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var response = await asC.PostAsync("/api/organizations/current/logo", Upload(TinyPng, "../../../" + fixture.TenantB + "/branding/owned.png"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(fixture.Blobs.Names, n => n.Contains("owned", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fixture.Blobs.Names, n => n.StartsWith($"{fixture.TenantB}/branding/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replacing_a_logo_removes_the_old_one_and_removing_it_removes_the_file()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        Assert.Equal(HttpStatusCode.OK, (await asC.PostAsync("/api/organizations/current/logo", Upload(TinyPng))).StatusCode);
        var firstBlob = fixture.Blobs.Names.Single(n => n.StartsWith($"{fixture.TenantC}/branding/", StringComparison.Ordinal));

        await asC.PostAsync("/api/organizations/current/logo", Upload(TinyPng.Append((byte)1).ToArray()));

        Assert.DoesNotContain(firstBlob, fixture.Blobs.Names);
        var brandingBlobs = fixture.Blobs.Names.Where(n => n.StartsWith($"{fixture.TenantC}/branding/", StringComparison.Ordinal)).ToList();
        Assert.Single(brandingBlobs);

        Assert.Equal(HttpStatusCode.OK, (await asC.DeleteAsync("/api/organizations/current/logo")).StatusCode);
        Assert.DoesNotContain(fixture.Blobs.Names, n => n.StartsWith($"{fixture.TenantC}/branding/", StringComparison.Ordinal));

        using var anonymous = fixture.AnonymousClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/branding/{fixture.TenantC}/logo")).StatusCode);
    }

    [Fact]
    public async Task One_committees_logo_is_never_served_under_another_committees_id()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        await asC.PostAsync("/api/organizations/current/logo", Upload(TinyPng));

        using var anonymous = fixture.AnonymousClient();
        // A and B uploaded nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/branding/{fixture.TenantA}/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/branding/{Guid.NewGuid()}/logo")).StatusCode);
    }

    [Fact]
    public async Task A_row_pointing_at_another_committees_blob_is_not_served_as_a_logo()
    {
        // Same rule as documents: the bytes are fetched by a stored path, so the
        // path must lie inside the committee's own branding prefix.
        var victim = $"{fixture.TenantB}/receipt-ZZB.pdf";
        await using (var identity = fixture.ContextWithNoTenant())
        {
            await identity.Organizations.Where(o => o.Id == fixture.TenantC)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.LogoBlob, victim));
        }

        using var anonymous = fixture.AnonymousClient();
        var response = await anonymous.GetAsync($"/api/branding/{fixture.TenantC}/logo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("PDF-BYTES", await response.Content.ReadAsStringAsync());

        await using var cleanup = fixture.ContextWithNoTenant();
        await cleanup.Organizations.Where(o => o.Id == fixture.TenantC).ExecuteUpdateAsync(s => s.SetProperty(o => o.LogoBlob, (string?)null));
    }

    [Fact]
    public async Task Another_committees_admin_cannot_change_this_committees_logo()
    {
        // There is no way to name another committee: the endpoint is /current.
        using var asA = fixture.ClientFor(fixture.TokenForA);
        await asA.PostAsync("/api/organizations/current/logo", Upload(TinyPng));

        await using var identity = fixture.ContextWithNoTenant();
        Assert.NotNull((await identity.Organizations.SingleAsync(o => o.Id == fixture.TenantA)).LogoBlob);
        Assert.Null((await identity.Organizations.SingleAsync(o => o.Id == fixture.TenantB)).LogoBlob);

        await asA.DeleteAsync("/api/organizations/current/logo");
    }
}
