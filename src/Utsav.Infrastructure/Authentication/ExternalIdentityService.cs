using Google.Apis.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Utsav.Domain.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Linq.Expressions;
using System.Security.Claims;

namespace Utsav.Infrastructure.Authentication;

public sealed class ExternalIdentityException(string message) : Exception(message);

/// <summary>
/// Resolves a verified external identity — Google or Microsoft Entra — to a
/// local user row, and makes sure that user has asked to join the committee.
///
/// Ported from <c>findOrCreateGoogleUser</c> and <c>requestAccess</c> in
/// <c>server/auth.mjs</c>, keeping the same resolution order and the same rule
/// that signing in never grants access by itself: a first sign-in creates a
/// <c>pending</c> membership that an admin still has to approve.
/// </summary>
public sealed class ExternalIdentityService(UtsavDbContext db, IConfiguration configuration)
{
    private static readonly Lazy<ConfigurationManager<OpenIdConnectConfiguration>> EntraMetadata = new(() =>
    {
        // Cached and refreshed automatically — signing keys rotate, and
        // re-fetching them on every sign-in would be both slow and rude.
        var tenant = Environment.GetEnvironmentVariable("PUJA_ENTRA_TENANT") ?? "common";
        return new ConfigurationManager<OpenIdConnectConfiguration>(
            $"https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
    });

    /// <summary>
    /// Verifies a Google Identity Services credential came from this app's
    /// client ID and wasn't tampered with, then resolves it to a user. Same
    /// guarantee <c>verifyGoogleToken</c> gave: callers can treat the result as
    /// trustworthy without re-checking it.
    /// </summary>
    public async Task<User> FromGoogleAsync(string credential, CancellationToken ct = default)
    {
        var clientId = configuration["Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ExternalIdentityException("Google sign-in is not configured on this server");
        }

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(credential, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [clientId],
            });
        }
        // Broad for the same reason as the Entra path below: a bad credential
        // is the caller's problem, not a server fault.
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new ExternalIdentityException(e.Message);
        }

        if (!payload.EmailVerified)
        {
            throw new ExternalIdentityException("That Google account's email isn't verified");
        }

        return await ResolveAsync(
            matchProvider: u => u.GoogleSub == payload.Subject,
            link: (user) => user.GoogleSub = payload.Subject,
            email: payload.Email,
            name: payload.Name,
            provider: "google",
            ct);
    }

    /// <summary>
    /// Validates an Entra-issued ID token against the tenant's published
    /// signing keys. Whether the person behind it authenticated with a work
    /// account, a personal Microsoft account, or Google federated into the
    /// tenant makes no difference here — by this point Entra has vouched for
    /// them and the token looks the same either way.
    /// </summary>
    public async Task<User> FromEntraAsync(string idToken, CancellationToken ct = default)
    {
        var clientId = configuration["AzureAd:ClientId"]
            ?? throw new ExternalIdentityException("Entra sign-in is not configured on this server");
        var tenantId = configuration["AzureAd:TenantId"];

        var metadata = await EntraMetadata.Value.GetConfigurationAsync(ct);
        var handler = new JwtSecurityTokenHandler();

        ClaimsPrincipal principal;
        try
        {
            principal = handler.ValidateToken(idToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                // Personal Microsoft accounts arrive under the shared consumers
                // tenant rather than this directory, so both issuers are valid
                // once the app registration allows them (Boss chose
                // work/school + personal accounts).
                ValidIssuers = BuildValidIssuers(tenantId, metadata.Issuer),
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = metadata.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
            }, out _);
        }
        // Deliberately broad. The token is attacker-controlled input, and every
        // way it can be wrong — malformed, expired, wrong audience, bad
        // signature, unparseable — is a 401 for the caller, never a 500 for us.
        // Catching the specific SecurityTokenException hierarchy missed
        // SecurityTokenMalformedException in practice and leaked a stack trace.
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new ExternalIdentityException(e.Message);
        }

        var objectId = principal.FindFirst("oid")?.Value
            ?? principal.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? throw new ExternalIdentityException("Token carried no object identifier");

        var email = principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? principal.FindFirst("email")?.Value
            ?? throw new ExternalIdentityException("Token carried no email address");

        var name = principal.FindFirst("name")?.Value;

        return await ResolveAsync(
            matchProvider: u => u.EntraObjectId == objectId,
            link: (user) => user.EntraObjectId = objectId,
            email: email,
            name: name,
            provider: "entra",
            ct);
    }

    private static string[] BuildValidIssuers(string? tenantId, string metadataIssuer)
    {
        var issuers = new List<string> { metadataIssuer };
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            issuers.Add($"https://login.microsoftonline.com/{tenantId}/v2.0");
            issuers.Add($"https://sts.windows.net/{tenantId}/");
        }

        // The tenant personal Microsoft accounts are homed in.
        issuers.Add("https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0");
        return [.. issuers.Distinct()];
    }

    /// <summary>
    /// Resolution order, unchanged from the Node original:
    ///   1. Already linked on this provider — the common case on repeat sign-in.
    ///   2. Same email exists from another sign-in method — link the two rather
    ///      than creating a second person with the same address.
    ///   3. Nobody home — create the user.
    /// Every path ends by requesting access, which is a no-op if they're
    /// already pending or active.
    /// </summary>
    private async Task<User> ResolveAsync(
        // Expression, not Func: EF has to translate this into a WHERE clause.
        // A compiled delegate can only be invoked in memory, so passing one
        // here makes the provider give up with "could not be translated"
        // rather than silently falling back to loading the table.
        Expression<Func<User, bool>> matchProvider,
        Action<User> link,
        string email,
        string? name,
        string provider,
        CancellationToken ct)
    {
        var normalisedEmail = email.Trim();

        var existing = await db.Users.AsTracking().FirstOrDefaultAsync(matchProvider, ct);
        if (existing is null)
        {
            existing = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Email == normalisedEmail, ct);
            if (existing is not null)
            {
                link(existing);
                await db.SaveChangesAsync(ct);
            }
        }

        if (existing is null)
        {
            existing = new User
            {
                Id = Guid.NewGuid(),
                Email = normalisedEmail,
                FullName = string.IsNullOrWhiteSpace(name) ? normalisedEmail.Split('@')[0] : name,
                AuthProvider = provider,
                PasswordHash = null,
                Role = "collector",
            };
            link(existing);
            db.Users.Add(existing);
            await db.SaveChangesAsync(ct);
        }

        await RequestAccessAsync(existing.Id, ct);
        return existing;
    }

    /// <summary>
    /// Idempotent: a repeat sign-in for an already-pending or already-active
    /// member changes nothing.
    /// </summary>
    public async Task RequestAccessAsync(Guid userId, CancellationToken ct = default)
    {
        var alreadyAMember = await db.OrgMembers
            .AnyAsync(m => m.OrgId == SessionService.DefaultOrgId && m.UserId == userId, ct);

        if (alreadyAMember)
        {
            return;
        }

        db.OrgMembers.Add(new OrgMember
        {
            Id = Guid.NewGuid(),
            OrgId = SessionService.DefaultOrgId,
            UserId = userId,
            Role = "viewer",
            Status = "pending",
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Local email + password signup, ported from <c>createUser</c>.</summary>
    public async Task<User> CreateLocalUserAsync(string email, string password, string? fullName, CancellationToken ct = default)
    {
        var normalisedEmail = email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == normalisedEmail, ct))
        {
            throw new ExternalIdentityException("An account with that email already exists");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalisedEmail,
            PasswordHash = ScryptPasswordHasher.Hash(password),
            FullName = string.IsNullOrWhiteSpace(fullName) ? normalisedEmail.Split('@')[0] : fullName,
            AuthProvider = "local",
            Role = "collector",
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        await RequestAccessAsync(user.Id, ct);
        return user;
    }
}
