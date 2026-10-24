namespace Utsav.Api.Security;

/// <summary>
/// Browser-side defences applied to every response. The session cookie is
/// httpOnly, but script that runs in the page can still act as the user, so the
/// policy's job is to stop injected markup from running at all, and to stop
/// other sites from framing the app.
///
/// What the app legitimately loads is short and fixed: its own bundle, the
/// Google Identity Services script and frame, Google Fonts, and the two sign-in
/// providers' endpoints. Anything else is refused.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' https://accounts.google.com/gsi/client; " +
        // Inline styles: the UI library positions popovers with style attributes.
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://accounts.google.com/gsi/style; " +
        "font-src 'self' data: https://fonts.gstatic.com; " +
        "img-src 'self' data: blob: https:; " +
        "connect-src 'self' https://accounts.google.com https://login.microsoftonline.com; " +
        "frame-src https://accounts.google.com; " +
        "worker-src 'self'; " +
        "manifest-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>
    /// Sets each header unless an endpoint already chose its own value (the logo
    /// endpoint is more specific than this default and must win).
    /// </summary>
    public static void Apply(HttpResponse response)
    {
        Set(response, "Content-Security-Policy", ContentSecurityPolicy);
        Set(response, "X-Content-Type-Options", "nosniff");
        Set(response, "X-Frame-Options", "DENY");
        Set(response, "Referrer-Policy", "strict-origin-when-cross-origin");
        Set(response, "Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
    }

    private static void Set(HttpResponse response, string name, string value)
    {
        if (!response.Headers.ContainsKey(name))
        {
            response.Headers[name] = value;
        }
    }
}
