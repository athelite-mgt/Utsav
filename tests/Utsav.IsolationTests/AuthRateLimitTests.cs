using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Credential guessing is the cheapest attack on a platform that holds several
/// committees' books, so the endpoints that take a secret are throttled per
/// client address. The shared test host raises the limit so the rest of the
/// suite can sign in freely; these tests run their own host with a small one.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class AuthRateLimitTests(DatabaseFixture fixture)
{
    private const int Limit = 3;

    private WebApplicationFactory<Program> LimitedHost() =>
        fixture.NewHost(builder => builder.UseSetting("Auth:RateLimit:PermitsPerMinute", Limit.ToString()));

    private static Task<HttpResponseMessage> BadLoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "wrong" });

    [Fact]
    public async Task Repeated_sign_in_attempts_are_throttled_after_the_limit()
    {
        await using var host = LimitedHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        for (var i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await BadLoginAsync(client)).StatusCode);
        }

        var throttled = await BadLoginAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);

        // The sign-in form shows `error` verbatim, so a throttled user is told to wait, not shown a blank failure.
        var body = await throttled.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Contains("try again", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(throttled.Headers.TryGetValues("Retry-After", out var retry) && int.Parse(retry.First()) > 0);
    }

    [Fact]
    public async Task Throttling_covers_sign_up_and_the_provider_sign_ins_too()
    {
        await using var host = LimitedHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // One shared budget per client across the credential endpoints: spreading guesses over
        // several of them must not multiply the allowance.
        for (var i = 0; i < Limit; i++)
        {
            await BadLoginAsync(client);
        }

        foreach (var path in new[] { "/api/auth/signup", "/api/auth/google", "/api/auth/entra" })
        {
            var response = await client.PostAsJsonAsync(path, new { });
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    [Fact]
    public async Task Ordinary_requests_are_not_throttled()
    {
        await using var host = LimitedHost();
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        for (var i = 0; i < Limit * 3; i++)
        {
            // 401, never 429: /me is not a credential endpoint.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        }
    }
}
