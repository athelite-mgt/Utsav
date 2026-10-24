namespace Utsav.Api.Authentication;

/// <summary>
/// The browser's session credential: an httpOnly cookie, so no script on the
/// page can read it. This replaces keeping the token in localStorage, where a
/// single cross-site-scripting bug anywhere in the app would have handed
/// every signed-in member's session to an attacker.
///
/// SameSite=Lax rather than Strict, deliberately: Strict drops the cookie on
/// the return leg of the Google and Microsoft sign-in redirects and breaks
/// social login. Lax still withholds it from cross-site POSTs, and every
/// state-changing request additionally needs the CSRF header (see
/// <c>SessionTokens.CsrfFor</c>).
///
/// <c>Secure</c> follows the request, not a setting: it is on whenever the
/// request arrived over HTTPS (App Service terminates TLS and forwards the
/// scheme, which Program.cs honours), and off only for plain-http localhost,
/// which browsers would otherwise refuse to store.
/// </summary>
public static class SessionCookie
{
    public const string Name = "utsav_session";

    /// <summary>The header every state-changing browser request must carry.</summary>
    public const string CsrfHeader = "X-Utsav-Csrf";

    public static void Issue(HttpContext http, string rawToken, DateTimeOffset expires) =>
        http.Response.Cookies.Append(Name, rawToken, Options(http, expires));

    public static void Clear(HttpContext http) =>
        http.Response.Cookies.Delete(Name, Options(http, expires: null));

    private static CookieOptions Options(HttpContext http, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
    };
}
