using Microsoft.AspNetCore.Http;
using Utsav.Api.Authentication;

namespace Utsav.UnitTests.Authentication;

public class SessionCookieOptionsTests
{
    private static string SetCookieFor(string scheme, Action<HttpContext> act)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        act(context);
        return context.Response.Headers.SetCookie.ToString();
    }

    [Fact]
    public void Over_https_the_cookie_is_secure_httponly_and_lax()
    {
        var header = SetCookieFor("https", c => SessionCookie.Issue(c, "token-value", DateTimeOffset.UtcNow.AddDays(30)));

        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Over_plain_http_it_is_not_secure_because_a_browser_would_refuse_to_store_it()
    {
        var header = SetCookieFor("http", c => SessionCookie.Issue(c, "token-value", DateTimeOffset.UtcNow.AddDays(30)));

        Assert.DoesNotContain("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clearing_it_expires_it_in_the_past_with_the_same_attributes()
    {
        var header = SetCookieFor("https", SessionCookie.Clear);

        Assert.Contains($"{SessionCookie.Name}=;", header);
        Assert.Contains("1970", header);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
    }
}
