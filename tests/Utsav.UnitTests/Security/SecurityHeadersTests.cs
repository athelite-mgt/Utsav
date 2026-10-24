using Microsoft.AspNetCore.Http;
using Utsav.Api.Security;
using Xunit;

namespace Utsav.UnitTests.Security;

public class SecurityHeadersTests
{
    private static Dictionary<string, string> Directives(string csp) =>
        csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(d => d.Split(' ', 2))
           .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");

    [Fact]
    public void Scripts_may_only_come_from_this_origin_and_the_google_sign_in_library()
    {
        var script = Directives(SecurityHeaders.ContentSecurityPolicy)["script-src"];

        Assert.Contains("'self'", script);
        Assert.Contains("https://accounts.google.com/gsi/client", script);
        // The whole point of the policy: injected markup cannot run.
        Assert.DoesNotContain("'unsafe-inline'", script);
        Assert.DoesNotContain("'unsafe-eval'", script);
        Assert.DoesNotContain("*", script);
    }

    [Fact]
    public void The_app_cannot_be_framed_and_cannot_embed_plugins_or_rebase_urls()
    {
        var d = Directives(SecurityHeaders.ContentSecurityPolicy);

        Assert.Equal("'none'", d["frame-ancestors"]);
        Assert.Equal("'none'", d["object-src"]);
        Assert.Equal("'self'", d["base-uri"]);
        Assert.Equal("'self'", d["form-action"]);
        Assert.Equal("'self'", d["default-src"]);
    }

    [Fact]
    public void Network_calls_are_limited_to_this_origin_and_the_two_sign_in_providers()
    {
        var connect = Directives(SecurityHeaders.ContentSecurityPolicy)["connect-src"].Split(' ');

        Assert.Equal(
            new[] { "'self'", "https://accounts.google.com", "https://login.microsoftonline.com" }.OrderBy(x => x),
            connect.OrderBy(x => x));
    }

    [Fact]
    public void Apply_sets_every_header_on_a_response()
    {
        var context = new DefaultHttpContext();

        SecurityHeaders.Apply(context.Response);

        var h = context.Response.Headers;
        Assert.Equal(SecurityHeaders.ContentSecurityPolicy, h["Content-Security-Policy"].ToString());
        Assert.Equal("nosniff", h["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", h["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", h["Referrer-Policy"].ToString());
        Assert.Contains("camera=()", h["Permissions-Policy"].ToString());
    }

    [Fact]
    public void Apply_does_not_overwrite_a_header_an_endpoint_already_chose()
    {
        // The logo endpoint sets its own nosniff; a later, more specific policy must win.
        var context = new DefaultHttpContext();
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";

        SecurityHeaders.Apply(context.Response);

        Assert.Equal("default-src 'none'", context.Response.Headers["Content-Security-Policy"].ToString());
    }
}
