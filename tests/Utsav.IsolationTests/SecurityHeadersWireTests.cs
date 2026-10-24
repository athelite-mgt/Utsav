using System.Net;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>The unit tests prove the policy; this proves it is actually attached to responses.</summary>
[Collection(nameof(DatabaseFixture))]
public class SecurityHeadersWireTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData("/api/auth/me")]       // an error response (401)
    [InlineData("/does-not-exist")]    // a 404 from the static-file / fallback path
    public async Task Every_response_carries_the_security_headers(string path)
    {
        using var client = fixture.AnonymousClient();

        var response = await client.GetAsync(path);

        Assert.True(response.Headers.TryGetValues("Content-Security-Policy", out var csp), "missing CSP");
        Assert.Contains("frame-ancestors 'none'", csp!.Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
