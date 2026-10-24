using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Audit;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Push;
using Utsav.Infrastructure.Storage;
using Utsav.Infrastructure.Tenancy;

namespace Utsav.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// The Node app ran one connection per serverless instance (<c>max: 1</c>)
    /// to stay under Supabase's session-pooler ceiling during concurrent cold
    /// starts. App Service is one long-lived process, so a real pool replaces
    /// that workaround — see TASKLIST §2.9.
    /// </summary>
    private const int MinPoolSize = 5;
    private const int MaxPoolSize = 20;

    public static IServiceCollection AddUtsavInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AzureSql")
            ?? throw new InvalidOperationException("Missing configuration: ConnectionStrings:AzureSql");

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            MinPoolSize = MinPoolSize,
            MaxPoolSize = MaxPoolSize,
        };

        // One instance per request: resolved by the auth handler, read by the
        // query filters, the SQL chokepoints and the RLS interceptor. Both
        // registrations return the same instance - consumers take
        // ITenantContext, only the auth handlers take TenantContext to call
        // Resolve.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<TenantSessionInterceptor>();
        services.AddDbContext<UtsavDbContext>((serviceProvider, options) =>
            options
                // Resolved per scope so the interceptor sees THAT request's
                // tenant, never a singleton's stale one.
                .AddInterceptors(serviceProvider.GetRequiredService<TenantSessionInterceptor>())
                .UseSqlServer(builder.ConnectionString, sql => sql
                    // Azure SQL throttles and fails over far more readily than a
                    // dedicated server — transient faults are an expected part of
                    // normal operation there, not an exceptional case. This uses
                    // the provider's own list of retryable Azure error numbers
                    // rather than matching on error text.
                    .EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))
                // Read-heavy app (dashboards, reports); opt IN to tracking on the
                // write paths that need it rather than opting out on every read.
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddAuditTrail(configuration);
        services.AddPushNotifications(configuration);
        services.AddDocumentStorage(configuration);

        return services;
    }

    /// <summary>
    /// TASKLIST §5. <see cref="CosmosClient"/> is thread-safe and meant to be
    /// a singleton for the app's lifetime, same as <c>HttpClient</c>.
    /// CamelCase serialization matches the documented partition key path
    /// (<c>/orgId</c> — TASKLIST §5.3) and keeps every property lowerCamel on
    /// the wire, Cosmos's own convention.
    /// </summary>
    private static IServiceCollection AddAuditTrail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ =>
        {
            var endpoint = configuration["Cosmos:Endpoint"]
                ?? throw new InvalidOperationException("Missing configuration: Cosmos:Endpoint");
            var key = configuration["Cosmos:Key"]
                ?? throw new InvalidOperationException("Missing configuration: Cosmos:Key");

            return new CosmosClient(endpoint, key, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                },
            });
        });

        services.AddSingleton<CosmosAuditSink>();
        services.AddSingleton<AuditChannel>();
        services.AddSingleton<IAuditSink, QueuedAuditSink>();
        services.AddHostedService<AuditQueueProcessor>();

        return services;
    }

    /// <summary>
    /// Mirrors <see cref="AddAuditTrail"/>'s shape — a channel plus a
    /// background drainer — but the config is optional here (see
    /// <see cref="VapidOptions.IsConfigured"/>): the app should start and
    /// run fully otherwise even before Boss has pasted the VAPID keys into
    /// Azure App Service configuration.
    /// </summary>
    private static IServiceCollection AddPushNotifications(this IServiceCollection services, IConfiguration configuration)
    {
        // Read by hand rather than via services.Configure<T>(IConfigurationSection)/.Bind()
        // — both need extra Microsoft.Extensions.Options/Configuration.Binder packages
        // this app doesn't otherwise reference, to save writing out three properties.
        services.Configure<VapidOptions>(o =>
        {
            // Keys match Azure App Service's Push__VapidPublicKey / Push__VapidPrivateKey
            // application settings (see VapidOptions's own remarks) — "Vapid"
            // in the key name, not just in the property name.
            o.PublicKey = configuration[$"{VapidOptions.SectionName}:VapidPublicKey"];
            o.PrivateKey = configuration[$"{VapidOptions.SectionName}:VapidPrivateKey"];
            o.Subject = configuration[$"{VapidOptions.SectionName}:Subject"];
        });
        services.AddSingleton<WebPushSender>();
        services.AddSingleton<PushChannel>();
        services.AddSingleton<IPushDispatcher, QueuedPushDispatcher>();
        services.AddHostedService<PushQueueProcessor>();

        return services;
    }

    /// <summary>
    /// Same "config-optional" shape as <see cref="AddPushNotifications"/> —
    /// see <see cref="BlobStorageOptions.IsConfigured"/>. Boss supplies the
    /// connection string later; until then <c>DocumentEndpoints</c> returns a
    /// clear 503 instead of the app failing to start or uploads half-working.
    /// </summary>
    private static IServiceCollection AddDocumentStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BlobStorageOptions>(o =>
        {
            o.ConnectionString = configuration[$"{BlobStorageOptions.SectionName}:ConnectionString"];
            o.ContainerName = configuration[$"{BlobStorageOptions.SectionName}:ContainerName"];
        });
        services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

        return services;
    }
}
