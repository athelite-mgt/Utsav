using System.Text.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Utsav.Api.Authentication;
using Utsav.Api.Asura;
using Utsav.Api.Endpoints;
using Utsav.Domain.Authorization;
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure;
using Utsav.Infrastructure.Audit;
using Utsav.Infrastructure.Push;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddUtsavInfrastructure(builder.Configuration);
// Registers nothing at all unless Asura:Enabled is true — see AddAsura. With
// the flag off there is no service to resolve and no route to reach, so this
// deploy cannot change how the app behaves for a committee member.
builder.Services.AddAsura(
    builder.Configuration,
    // Startup-time logger: a misconfigured chain should be visible in the log
    // at boot, not discovered by a treasurer asking the first question.
    LoggerFactory.Create(b => b.AddConsole()).CreateLogger("Asura"));
builder.Services.AddHttpContextAccessor();

// The whole frontend (Node/src/types/db.ts) expects snake_case field names
// straight off the DB — it's a straight passthrough in Node (`res.json(rows)`
// on driver rows), never a camelCase transformation layer. Set once, globally,
// rather than a JsonPropertyName attribute on every DTO property.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
});

builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<ExternalIdentityService>();
builder.Services.AddScoped<CommitteeBootstrapper>();
builder.Services.AddScoped<ApiKeyService>();
builder.Services
    .AddAuthentication(LegacySessionAuthenticationDefaults.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, LegacySessionAuthenticationHandler>(
        LegacySessionAuthenticationDefaults.SchemeName, _ => { })
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationDefaults.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.ActiveMember, policy =>
        policy.RequireClaim(LegacySessionAuthenticationDefaults.StatusClaimType, "active"))
    .AddPolicy(AuthorizationPolicies.Admin, policy => policy
        .RequireClaim(LegacySessionAuthenticationDefaults.StatusClaimType, "active")
        .RequireRole(AppRole.Admin.ToWire()))
    // The one policy an API key can satisfy: a signed-in member with
    // audit-read, OR a valid key. Both schemes are listed so a browser
    // session still works on the same route (that's what the Settings page
    // uses to offer a "download it yourself" link), while the agent's key
    // needs no session at all. Deliberately not folded into ActiveMember —
    // a key must never reach a write route.
    .AddPolicy(AuthorizationPolicies.ReportExport, policy => policy
        .AddAuthenticationSchemes(
            LegacySessionAuthenticationDefaults.SchemeName,
            ApiKeyAuthenticationDefaults.SchemeName)
        .RequireAssertion(ctx =>
            ctx.User.Identity?.AuthenticationType == ApiKeyAuthenticationDefaults.SchemeName
            || (ctx.User.HasClaim(LegacySessionAuthenticationDefaults.StatusClaimType, "active")
                // Fails closed: an unrecognised or missing role is null here
                // and is refused rather than guessed at.
                && ctx.User.CallerRole() is { } role
                && PermissionMatrix.Allows(role, Permission.ReadAudit))));
// Backs RequirePermission(Permission.X) everywhere else — see
// PermissionAuthorizationHandler and PermissionMatrix, the one place the
// actual role-to-permission table lives.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// QuestPDF Community licence: free for organisations under USD 1M annual
// revenue, which a neighbourhood puja committee is comfortably within. Set
// before any document is generated or the library refuses to render.
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var app = builder.Build();

// DynamicSql is what every write endpoint funnels through (27 call sites) —
// see its own remarks for why audit capture is wired here, once, rather than
// as a constructor-injected dependency at each call site.
DynamicSql.Configure(
    app.Services.GetRequiredService<IHttpContextAccessor>(),
    app.Services.GetRequiredService<IAuditSink>(),
    app.Services.GetRequiredService<IPushDispatcher>());

// Azure App Service terminates TLS at the platform and forwards plain HTTP to
// Kestrel, tagging the original scheme in X-Forwarded-Proto. Without this,
// UseHttpsRedirection() never sees a request it believes is already HTTPS and
// redirects every single one — an infinite loop, since the redirect target is
// itself "http" from Kestrel's point of view. Must run before anything that
// inspects the scheme.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// The built React app (wwwroot, populated by the frontend build on `dotnet
// publish` — see Utsav.Api.csproj) is served from the same origin as the API.
// Local dev doesn't need this: Vite serves the frontend itself and proxies
// /api to this process on a different port.
//
// Caching is split deliberately. Vite content-hashes every filename under
// /assets (index-DSS0Fm-E.js and so on), so a new deploy always produces new
// URLs — those are safe to cache forever. index.html is the opposite: it's
// the one file that *references* those hashed names, and without an explicit
// no-cache header here, a browser can serve a stale cached copy of it after a
// deploy and go looking for /assets files that no longer exist. Found by
// hitting exactly that in the browser tool mid-deployment-verification — a
// tab that had loaded the app before a fix shipped kept showing the old
// build until forced past its own cache, and a real returning committee
// member would hit the same thing with no way to force it themselves.
var staticFileOptions = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFileOptions);

// A brand-new database gets its organization and reference data here, so
// deploying for another committee is: create database, run migrations, start.
// No-op once an organization exists, so established deployments are untouched.
//
// Deliberately not allowed to bring the app down. This runs before app.Run(),
// which means before Kestrel binds its port at all — a slow or unreachable
// database at this point (a firewall rule not yet applied, a transient
// network blip) previously meant the whole container never started serving
// anything, not even static files or a health check, and the platform's own
// warm-up probe would eventually kill it outright. A cold start that can't
// reach the database is exactly when the app most needs to still come up and
// report that clearly, rather than disappear.
try
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CommitteeBootstrapper>().EnsureSeededAsync();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Committee bootstrap failed — continuing startup anyway. " +
        "If this is a brand-new database, it still needs seeding once connectivity is restored.");
}

// Same reasoning as the committee bootstrap above, for the Cosmos side: must
// not be why the app fails to start, since the audit trail is deliberately
// not on the critical path for a committee member's actual save (TASKLIST §5.6).
try
{
    await app.Services.GetRequiredService<CosmosAuditSink>().EnsureContainerExistsAsync();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Cosmos audit container bootstrap failed — continuing startup anyway. " +
        "Audit writes will silently fail at the queue processor until this is fixed.");
}

// Same reasoning again, for document storage. A no-op until Boss supplies
// Storage:ConnectionString — see BlobStorageOptions.IsConfigured.
try
{
    await app.Services.GetRequiredService<IBlobStorageService>().EnsureContainerExistsAsync();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Blob storage container bootstrap failed — continuing startup anyway. " +
        "Document uploads will fail until this is fixed.");
}

// A request that reaches data access without a committee is refused, not
// served from an arbitrary tenant. 403 rather than 401: the caller is
// authenticated, they simply have not selected a committee they belong to.
// Deliberately narrow - every other exception is rethrown untouched, so the
// framework's normal error behaviour is unchanged.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception e) when (e is TenantNotResolvedException or TenancyViolationException)
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        // A violation is a programming error (a statement the executor could
        // not prove tenant-safe). It fails closed with the same 403, but it
        // must be loud in the logs.
        if (e is TenancyViolationException)
        {
            app.Logger.LogError(e, "Refused an unscoped statement on {Path}", context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "No committee selected" });
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapReferenceEndpoints();
app.MapModuleEndpoints();
app.MapFundPositionEndpoint();
app.MapAdvanceEndpoints();
app.MapContractEndpoints();
app.MapVendorIssueEndpoints();
app.MapAdminEndpoints();
app.MapAuditEndpoints();
app.MapPushEndpoints();
app.MapDocumentEndpoints();
app.MapCollectionEndpoints();
app.MapFeastTokenEndpoints();
app.MapAuditReportEndpoint();
app.MapApiKeyEndpoints();
app.MapPujaCalendarEndpoints();
app.MapAsuraEndpoints();
// Last: its "{table}" route would otherwise shadow the specific ones above.
app.MapCrudEndpoints();

// Anything that isn't /api/* and isn't a static file is a client-side route
// (react-router) — hand it index.html and let the browser router take over.
// Mapped last, and MapFallbackToFile only activates for GET requests that
// matched nothing above, so it can't shadow a real API route.
app.MapFallbackToFile("index.html", staticFileOptions);

app.Run();
