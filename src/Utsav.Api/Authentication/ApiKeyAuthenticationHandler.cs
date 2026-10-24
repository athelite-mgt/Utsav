using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Utsav.Infrastructure.Authentication;

namespace Utsav.Api.Authentication;

public static class ApiKeyAuthenticationDefaults
{
    public const string SchemeName = "ApiKey";

    /// <summary>The issuing record's id, so endpoints can attribute a request to one integration.</summary>
    public const string KeyIdClaimType = "puja:api_key_id";
}

/// <summary>
/// Authenticates a machine caller presenting an <see cref="Utsav.Infrastructure.Data.Entities.ApiKey"/>
/// — currently the committee's Hermes agent pulling the audit report on a
/// schedule.
///
/// Accepted as either <c>X-Api-Key: puja_sk_…</c> or
/// <c>Authorization: Bearer puja_sk_…</c>. The bearer form shares a header
/// with <see cref="LegacySessionAuthenticationHandler"/>, so the
/// <c>puja_sk_</c> marker is what tells the two apart: a value without it is
/// left alone (NoResult) for the session scheme to claim, rather than being
/// failed here.
///
/// An API key is not a member session. It carries no membership status
/// claim, so <see cref="AuthorizationPolicies.ActiveMember"/> — and every
/// route behind it, which is all the write routes — rejects it. The only
/// thing it satisfies is a policy that explicitly names this scheme, which
/// today is the read-only report export alone.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyService apiKeys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = PresentedKey();
        if (presented is null) return AuthenticateResult.NoResult();

        Utsav.Infrastructure.Data.Entities.ApiKey? key;
        try
        {
            key = await apiKeys.ResolveAsync(presented, Context.RequestAborted);
        }
        catch (Exception ex)
        {
            // Fail closed, and never surface the reason. A database problem
            // on the auth path (unreachable, or the api_keys table not yet
            // migrated) previously produced a 500 for anyone who presented
            // any key at all — which both leaks that something is wrong
            // internally and makes an operational fault look like a code
            // one. Logged so operators still see it; the caller just gets
            // the same 401 an invalid key gets.
            Logger.LogError(ex, "API key lookup failed — rejecting the request.");
            return AuthenticateResult.Fail("Invalid API key");
        }

        if (key is null)
        {
            // Same answer for "no such key" and "revoked" — telling them
            // apart would confirm a guessed key had once been real.
            return AuthenticateResult.Fail("Invalid API key");
        }

        // Awaited, not fire-and-forget: ApiKeyService shares the request's
        // scoped DbContext, and letting this run loose meant it raced the
        // report's own queries on the same connection — "The connection does
        // not support MultipleActiveResultSets", every time. It's one
        // indexed UPDATE on a route called on a schedule, so the cost of
        // sequencing it is irrelevant; TouchAsync still swallows its own
        // failures so a bookkeeping write can't fail a good report.
        await apiKeys.TouchAsync(key.Id, Context.RequestAborted);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, key.Id.ToString()),
                new Claim(ClaimTypes.Name, key.Name),
                new Claim(ApiKeyAuthenticationDefaults.KeyIdClaimType, key.Id.ToString()),
            ],
            ApiKeyAuthenticationDefaults.SchemeName);

        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationDefaults.SchemeName));
    }

    private string? PresentedKey()
    {
        var header = Request.Headers["X-Api-Key"].ToString();
        if (header.StartsWith(ApiKeyService.KeyPrefix, StringComparison.Ordinal)) return header;

        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            var token = auth["Bearer ".Length..];
            if (token.StartsWith(ApiKeyService.KeyPrefix, StringComparison.Ordinal)) return token;
        }

        return null;
    }
}
