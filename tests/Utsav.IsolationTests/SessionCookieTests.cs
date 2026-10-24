using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Api.Authentication;
using Utsav.Infrastructure.Authentication;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// The session lives in an httpOnly cookie no script can read, every
/// state-changing request must also carry a token another site cannot obtain,
/// and a session has a bounded life. These hold the app to each of those.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class SessionCookieTests(DatabaseFixture fixture)
{
    private const string KnownPassword = "correct horse battery staple";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? SessionCookieOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(SessionCookie.Name + "=", StringComparison.Ordinal))
            : null;

    private static string ValueOf(string setCookie) =>
        setCookie[(SessionCookie.Name.Length + 1)..].Split(';')[0];

    private async Task<(HttpResponseMessage Response, string SetCookie, string Raw)> SignInAsync(string email = "both@test.local")
    {
        using var anonymous = fixture.AnonymousClient();
        var response = await anonymous.PostAsJsonAsync("/api/auth/login", new { email, password = KnownPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = SessionCookieOf(response);
        Assert.NotNull(cookie);
        return (response, cookie!, ValueOf(cookie!));
    }

    // ---- the cookie ------------------------------------------------------

    [Fact]
    public async Task Signing_in_hands_the_browser_an_httponly_cookie_and_never_puts_the_token_in_the_body()
    {
        var (response, setCookie, raw) = await SignInAsync();

        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);

        // The token must not ALSO come back where a script could read it: that
        // would put it straight back within reach and defeat the cookie.
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(raw, body);
        Assert.False(JsonDocument.Parse(body).RootElement.TryGetProperty("token", out _));
    }

    [Fact]
    public async Task What_the_database_stores_is_a_hash_of_the_cookie_not_the_cookie()
    {
        var (_, _, raw) = await SignInAsync();

        await using var identity = fixture.ContextWithNoTenant();
        Assert.False(await identity.Sessions.AnyAsync(s => s.Token == raw));
        Assert.True(await identity.Sessions.AnyAsync(s => s.Token == SessionTokens.Hash(raw)));
    }

    [Fact]
    public async Task The_cookie_signs_the_browser_in_and_the_page_is_given_a_csrf_token_that_is_not_the_session()
    {
        var (_, _, raw) = await SignInAsync();
        using var client = fixture.ClientFor(raw);

        var me = await JsonAsync(await client.GetAsync("/api/auth/me"));

        Assert.Equal("both@test.local", me.GetProperty("user").GetProperty("email").GetString());
        var csrf = me.GetProperty("csrf_token").GetString()!;
        Assert.Equal(SessionTokens.CsrfFor(raw), csrf);
        Assert.NotEqual(raw, csrf);
    }

    [Fact]
    public async Task Every_sign_in_gets_a_fresh_token_so_a_planted_one_is_never_adopted()
    {
        var first = (await SignInAsync()).Raw;
        var second = (await SignInAsync()).Raw;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Signing_up_also_signs_the_new_account_in_with_a_cookie()
    {
        using var anonymous = fixture.AnonymousClient();

        var response = await anonymous.PostAsJsonAsync("/api/auth/signup", new
        {
            email = $"new-{Guid.NewGuid():N}@test.local", password = "a-long-enough-password", full_name = "New Person",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(SessionCookieOf(response));
    }

    // ---- the bearer path is gone -----------------------------------------

    [Fact]
    public async Task A_session_token_in_an_authorization_header_is_no_longer_accepted()
    {
        var (_, _, raw) = await SignInAsync();
        using var client = fixture.AnonymousClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", raw);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_cookie_is_simply_not_signed_in()
    {
        using var client = fixture.ClientWithCookieOnly(SessionTokens.Generate());

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    // ---- CSRF ------------------------------------------------------------

    [Fact]
    public async Task A_state_changing_request_with_the_cookie_but_no_csrf_token_is_refused()
    {
        // What a cross-site form post looks like: the browser attaches the
        // cookie by itself, but the other site cannot add the header.
        using var crossSite = fixture.ClientWithCookieOnly(fixture.TokenForC);

        var response = await crossSite.PostAsJsonAsync("/api/vendors", new { name = "CSRF probe" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("CSRF", await response.Content.ReadAsStringAsync());
        await using var inC = fixture.ContextFor(fixture.TenantC);
        Assert.False(await inC.Vendors.AnyAsync(v => v.Name == "CSRF probe"));
    }

    [Fact]
    public async Task Another_sessions_csrf_token_does_not_work_for_this_one()
    {
        using var client = fixture.ClientWithCookieOnly(fixture.TokenForC);
        client.DefaultRequestHeaders.Add(SessionCookie.CsrfHeader, SessionTokens.CsrfFor(fixture.TokenForA));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/vendors", new { name = "borrowed csrf" })).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task A_made_up_csrf_token_does_not_work(string guess)
    {
        using var client = fixture.ClientWithCookieOnly(fixture.TokenForC);
        client.DefaultRequestHeaders.Add(SessionCookie.CsrfHeader, guess);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/vendors", new { name = "guessed csrf" })).StatusCode);
    }

    [Fact]
    public async Task The_right_csrf_token_lets_the_request_through()
    {
        using var client = fixture.ClientFor(fixture.TokenForC);

        var response = await client.PostAsJsonAsync("/api/vendors", new { name = "legit csrf vendor" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Reading_needs_no_csrf_token()
    {
        using var client = fixture.ClientWithCookieOnly(fixture.TokenForC);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/zones")).StatusCode);
    }

    [Fact]
    public async Task Signing_in_needs_no_csrf_token_because_there_is_no_session_yet()
    {
        using var anonymous = fixture.AnonymousClient();

        var response = await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "both@test.local", password = KnownPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Switching_committee_and_signing_out_are_csrf_protected_too()
    {
        var (_, _, raw) = await SignInAsync();
        using var crossSite = fixture.ClientWithCookieOnly(raw);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await crossSite.PostAsJsonAsync("/api/auth/switch-org", new { org_id = fixture.TenantA })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await crossSite.PostAsync("/api/auth/logout", null)).StatusCode);

        // And a forged logout did not end the real session.
        await using var identity = fixture.ContextWithNoTenant();
        Assert.True(await identity.Sessions.AnyAsync(s => s.Token == SessionTokens.Hash(raw)));
    }

    // ---- signing out and expiry ------------------------------------------

    [Fact]
    public async Task Signing_out_ends_the_session_on_the_server_and_clears_the_cookie()
    {
        var (_, _, raw) = await SignInAsync();
        using var client = fixture.ClientFor(raw);

        var response = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cleared = SessionCookieOf(response);
        Assert.NotNull(cleared);
        Assert.Equal("", ValueOf(cleared!));

        // Not just forgotten by the browser: dead on the server, so a copy of the cookie is useless.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await using var identity = fixture.ContextWithNoTenant();
        Assert.False(await identity.Sessions.AnyAsync(s => s.Token == SessionTokens.Hash(raw)));
    }

    [Fact]
    public async Task A_session_past_its_absolute_lifetime_is_refused_and_removed()
    {
        var raw = SessionTokens.Generate();
        await using (var identity = fixture.ContextWithNoTenant())
        {
            var session = DatabaseFixture.Live(raw, fixture.UserC, fixture.TenantC);
            session.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            identity.Sessions.Add(session);
            await identity.SaveChangesAsync();
        }

        using var client = fixture.ClientFor(raw);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        await using var check = fixture.ContextWithNoTenant();
        Assert.False(await check.Sessions.AnyAsync(s => s.Token == SessionTokens.Hash(raw)));
    }

    [Fact]
    public async Task A_session_idle_for_too_long_is_refused_even_though_its_lifetime_is_not_up()
    {
        var raw = SessionTokens.Generate();
        await using (var identity = fixture.ContextWithNoTenant())
        {
            var session = DatabaseFixture.Live(raw, fixture.UserC, fixture.TenantC);
            session.LastSeenAt = DateTime.UtcNow - SessionService.IdleTimeout - TimeSpan.FromHours(1);
            identity.Sessions.Add(session);
            await identity.SaveChangesAsync();
        }

        using var client = fixture.ClientFor(raw);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }
}
