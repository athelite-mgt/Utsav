using Utsav.Api.Security;
using Xunit;

namespace Utsav.UnitTests.Security;

public class ForwardedHeadersSetupTests
{
    [Fact]
    public void Behind_the_platform_proxy_the_client_address_and_scheme_are_taken_from_its_headers()
    {
        // Azure's front end is not loopback. If its headers were ignored, every visitor would share
        // one address: one rate-limit bucket for the whole product and a redirect loop on HTTPS.
        var options = ForwardedHeadersSetup.Create(behindPlatformProxy: true);

        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.True(options.ForwardedHeaders.HasFlag(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor));
        Assert.True(options.ForwardedHeaders.HasFlag(Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto));
        // One hop only: the entry our own proxy appended, never one the client could have forged.
        Assert.Equal(1, options.ForwardLimit);
    }

    [Fact]
    public void Anywhere_else_forwarded_headers_are_only_trusted_from_loopback()
    {
        var options = ForwardedHeadersSetup.Create(behindPlatformProxy: false);

        Assert.NotEmpty(options.KnownIPNetworks);
    }

    [Fact]
    public void App_Service_is_recognised_by_its_site_name_setting()
    {
        Assert.True(ForwardedHeadersSetup.IsAppService(name => name == "WEBSITE_SITE_NAME" ? "utsav" : null));
        Assert.False(ForwardedHeadersSetup.IsAppService(_ => null));
        Assert.False(ForwardedHeadersSetup.IsAppService(_ => ""));
    }
}
