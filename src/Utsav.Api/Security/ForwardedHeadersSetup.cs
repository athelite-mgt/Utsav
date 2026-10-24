using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Utsav.Api.Security;

/// <summary>
/// Azure App Service terminates TLS and forwards to Kestrel from an address
/// that is not loopback. ASP.NET Core's default is to ignore forwarded headers
/// from anything but loopback, so without this every visitor would appear to
/// come from the one proxy address: a redirect loop on HTTPS, and a single
/// rate-limit bucket shared by every user of the product.
/// </summary>
public static class ForwardedHeadersSetup
{
    public static ForwardedHeadersOptions Create(bool behindPlatformProxy)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            // The one entry the platform's front end appended; never one the client could have supplied.
            ForwardLimit = 1,
        };

        if (behindPlatformProxy)
        {
            // The worker is reachable only through the platform's front end, so its headers are the
            // trusted ones. Clearing the lists is Microsoft's documented App Service configuration.
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        }

        return options;
    }

    /// <summary>App Service always sets WEBSITE_SITE_NAME; nothing else in this stack does.</summary>
    public static bool IsAppService(Func<string, string?> getEnvironmentVariable) =>
        !string.IsNullOrEmpty(getEnvironmentVariable("WEBSITE_SITE_NAME"));
}
