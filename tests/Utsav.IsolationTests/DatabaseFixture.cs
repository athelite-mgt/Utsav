using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Utsav.Domain.Authentication;
using Utsav.Infrastructure.Audit;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using Utsav.Infrastructure.Storage;
using Utsav.Infrastructure.Tenancy;
using Utsav.IsolationTests.Support;

namespace Utsav.IsolationTests;

/// <summary>What was seeded for one committee, so tests can name its rows.</summary>
public sealed record TenantData(
    Guid Org, string Marker, Guid Year, Guid Zone, Guid Collector, Guid Donor, Guid Subscription,
    Guid Vendor, Guid Contract, Guid Expense, Guid BudgetLine, Guid CashEntry, Guid Advance,
    Guid WorkflowItem, Guid Document);

/// <summary>
/// Two committees with deliberately parallel data, so a leak shows up as the
/// wrong committee's marker or id in a response rather than as an obviously
/// foreign row. Each committee's text is stamped with a marker ("ZZA"/"ZZB")
/// and amounts differ, so both a name leak and an aggregate leak are visible.
///
/// Everything tenancy-related is real: a real SQL Server database, the real
/// migrations (row-level security included), the application connecting as
/// the least-privilege login it uses in production, and the real API booted
/// in-process. Only Cosmos and Blob storage are replaced.
///
/// Requires MSSQL_SA_PASSWORD (to create the database and run migrations) and
/// UTSAV_APP_PASSWORD (the application login's password), and the container
/// from docker-compose.dev.yml. A missing prerequisite FAILS the suite rather
/// than skipping it, so an unconfigured machine can never read as green.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string MarkerA = "ZZA";
    public const string MarkerB = "ZZB";

    public Guid TenantA { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid TenantB { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>Admin of both committees: the switcher user.</summary>
    public Guid UserInBoth { get; } = Guid.NewGuid();

    /// <summary>Finance member of committee A only.</summary>
    public Guid UserInAOnly { get; } = Guid.NewGuid();

    /// <summary>A real account with no membership anywhere.</summary>
    public Guid UserOutsider { get; } = Guid.NewGuid();

    public string TokenForA { get; } = "aaaa" + Guid.NewGuid().ToString("N");
    public string TokenForB { get; } = "bbbb" + Guid.NewGuid().ToString("N");
    public string TokenUnpinned { get; } = "cccc" + Guid.NewGuid().ToString("N");
    public string TokenAOnly { get; } = "dddd" + Guid.NewGuid().ToString("N");
    public string TokenOutsider { get; } = "eeee" + Guid.NewGuid().ToString("N");

    public TenantData A { get; private set; } = null!;
    public TenantData B { get; private set; } = null!;

    public InMemoryAuditSink Audit { get; } = new();
    public InMemoryBlobStore Blobs { get; } = new();

    private string _database = "";
    private string _saPassword = "";
    private string _appPassword = "";
    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _saPassword = Environment.GetEnvironmentVariable("MSSQL_SA_PASSWORD")
            ?? throw new InvalidOperationException(
                "MSSQL_SA_PASSWORD is not set. Start the container (docker compose -f docker-compose.dev.yml up -d) " +
                "and export it from .env. The isolation suite never skips: an unconfigured run must not read as green.");
        _appPassword = Environment.GetEnvironmentVariable("UTSAV_APP_PASSWORD")
            ?? throw new InvalidOperationException("UTSAV_APP_PASSWORD is not set (see README, 'Database principals').");

        _database = $"utsav_iso_{Guid.NewGuid():N}";

        await ProvisionDatabaseAsync();
        await SeedAsync();

        // Read by the app's own configuration before the host is built; set
        // as an environment variable because that is visible to Program.cs's
        // top-level code, which a late test-time setting is not.
        Environment.SetEnvironmentVariable("ConnectionStrings__AzureSql", AppConnection());

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // Cosmos is not available offline. The audit worker is removed
                // and the sink replaced, which also lets tests assert which
                // tenant each audit event was filed under.
                var worker = services.Where(d => d.ImplementationType == typeof(AuditQueueProcessor)).ToList();
                foreach (var descriptor in worker)
                {
                    services.Remove(descriptor);
                }

                services.RemoveAll<IAuditSink>();
                services.AddSingleton<IAuditSink>(Audit);
                services.RemoveAll<IBlobStorageService>();
                services.AddSingleton<IBlobStorageService>(Blobs);
            }));
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        try
        {
            await using var master = new SqlConnection(SaConnection("master"));
            await master.OpenAsync();
            await using var drop = master.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}];";
            await drop.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort: a leftover throwaway database is harmless.
        }
    }

    /// <summary>
    /// A context scoped to one committee, connecting as the least-privilege
    /// application login and stamping the tenant onto the connection exactly
    /// as the app does, so row-level security applies to it.
    /// </summary>
    public UtsavDbContext ContextFor(Guid tenant)
    {
        var context = new TenantContext();
        context.Resolve(tenant);
        return Build(context);
    }

    /// <summary>A context with no tenant: sees nothing in a protected table.</summary>
    public UtsavDbContext ContextWithNoTenant() => Build(new TenantContext());

    private UtsavDbContext Build(TenantContext tenant) =>
        new(new DbContextOptionsBuilder<UtsavDbContext>()
                .UseSqlServer(AppConnection())
                .AddInterceptors(new TenantSessionInterceptor(tenant))
                .Options,
            tenant);

    /// <summary>An HTTP client whose bearer token pins it to one committee.</summary>
    public HttpClient ClientFor(string token)
    {
        var client = _factory!.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return client;
    }

    public HttpClient AnonymousClient() => _factory!.CreateClient();

    public IServiceProvider Services => _factory!.Services;

    /// <summary>
    /// Runs a query as the high-privilege principal and returns the first
    /// column as strings. Used to ask SQL Server itself what is protected, so
    /// the tests do not depend on a list this codebase maintains by hand.
    /// </summary>
    public async Task<List<string>> QueryAsAdminAsync(string sql)
    {
        await using var connection = new SqlConnection(SaConnection(_database));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return result;
    }

    public string AppConnection() => new SqlConnectionStringBuilder
    {
        DataSource = "localhost,1433",
        InitialCatalog = _database,
        UserID = "utsav_app",
        Password = _appPassword,
        TrustServerCertificate = true,
        Encrypt = true,
    }.ConnectionString;

    private string SaConnection(string database) => new SqlConnectionStringBuilder
    {
        DataSource = "localhost,1433",
        InitialCatalog = database,
        UserID = "sa",
        Password = _saPassword,
        TrustServerCertificate = true,
        Encrypt = true,
    }.ConnectionString;

    private async Task ProvisionDatabaseAsync()
    {
        await using (var master = new SqlConnection(SaConnection("master")))
        {
            await master.OpenAsync();
            await using var command = master.CreateCommand();
            // The login is server-wide and shared by every throwaway database.
            command.CommandText = $"""
                IF SUSER_ID(N'utsav_app') IS NULL
                    EXEC(N'CREATE LOGIN utsav_app WITH PASSWORD = N''{_appPassword.Replace("'", "''''")}'', CHECK_POLICY = OFF;');
                CREATE DATABASE [{_database}];
                """;
            await command.ExecuteNonQueryAsync();
        }

        // Migrations run as the high-privilege principal, exactly as in
        // deployment; the application never gets these rights.
        await using (var migrator = new UtsavDbContext(
            new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer(SaConnection(_database)).Options))
        {
            await migrator.Database.MigrateAsync();
        }

        await using var db = new SqlConnection(SaConnection(_database));
        await db.OpenAsync();
        await using var grant = db.CreateCommand();
        grant.CommandText = """
            IF USER_ID(N'utsav_app') IS NULL CREATE USER utsav_app FOR LOGIN utsav_app;
            ALTER ROLE utsav_app_role ADD MEMBER utsav_app;
            """;
        await grant.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync()
    {
        var hash = ScryptPasswordHasher.Hash("correct horse battery staple");

        // Identity and membership tables are not tenant-scoped, so these go in
        // with no tenant resolved.
        await using (var identity = ContextWithNoTenant())
        {
            identity.Organizations.AddRange(
                new Organization { Id = TenantA, Name = "Durga Committee", Slug = "durga", City = "Barrackpore", IsActive = true },
                new Organization { Id = TenantB, Name = "Kali Committee", Slug = "kali", City = "Barrackpore", IsActive = true });

            identity.Users.AddRange(
                new User { Id = UserInBoth, Email = "both@test.local", FullName = "Both", AuthProvider = "local", PasswordHash = hash, Role = "viewer" },
                new User { Id = UserInAOnly, Email = "aonly@test.local", FullName = "A Only", AuthProvider = "local", PasswordHash = hash, Role = "viewer" },
                new User { Id = UserOutsider, Email = "outsider@test.local", FullName = "Outsider", AuthProvider = "local", PasswordHash = hash, Role = "viewer" });

            identity.OrgMembers.AddRange(
                new OrgMember { Id = Guid.NewGuid(), OrgId = TenantA, UserId = UserInBoth, Role = "admin", Status = "active" },
                new OrgMember { Id = Guid.NewGuid(), OrgId = TenantB, UserId = UserInBoth, Role = "admin", Status = "active" },
                new OrgMember { Id = Guid.NewGuid(), OrgId = TenantA, UserId = UserInAOnly, Role = "finance", Status = "active" });

            identity.Sessions.AddRange(
                new Session { Token = TokenForA, UserId = UserInBoth, OrgId = TenantA },
                new Session { Token = TokenForB, UserId = UserInBoth, OrgId = TenantB },
                new Session { Token = TokenUnpinned, UserId = UserInBoth, OrgId = null },
                new Session { Token = TokenAOnly, UserId = UserInAOnly, OrgId = TenantA },
                new Session { Token = TokenOutsider, UserId = UserOutsider, OrgId = null });

            await identity.SaveChangesAsync();
        }

        A = await SeedTenantAsync(TenantA, MarkerA, collected: 5000m, spent: 1000m, forecast: 20000m, advance: 1500m);
        B = await SeedTenantAsync(TenantB, MarkerB, collected: 7000m, spent: 2000m, forecast: 30000m, advance: 2500m);
    }

    private async Task<TenantData> SeedTenantAsync(
        Guid tenant, string marker, decimal collected, decimal spent, decimal forecast, decimal advance)
    {
        // Through the tenant's own context: the row-level-security BLOCK
        // predicates would reject writes made with the wrong (or no) tenant.
        await using var db = ContextFor(tenant);

        var year = Guid.NewGuid();
        var zone = Guid.NewGuid();
        var collector = Guid.NewGuid();
        var donor = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var vendor = Guid.NewGuid();
        var contract = Guid.NewGuid();
        var expense = Guid.NewGuid();
        var budget = Guid.NewGuid();
        var cash = Guid.NewGuid();
        var adv = Guid.NewGuid();
        var workflow = Guid.NewGuid();
        var document = Guid.NewGuid();

        db.PujaYears.Add(new PujaYear
        {
            Id = year, OrgId = tenant, Year = 2026, Name = $"Puja 2026 {marker}", Target = 100000m,
            ProjectionPct = 6.5m, IsActive = true,
        });
        await db.SaveChangesAsync();

        db.Zones.Add(new Zone { Id = zone, OrgId = tenant, Name = $"16 Feet {marker}", SortOrder = 1 });
        db.Collectors.Add(new Collector { Id = collector, OrgId = tenant, Name = $"Collector {marker}", Active = true, SortOrder = 1 });
        db.Lookups.Add(new Lookup { Id = Guid.NewGuid(), OrgId = tenant, Category = "payment_mode", Label = $"Cash {marker}", SortOrder = 1, Active = true });
        db.Vendors.Add(new Vendor { Id = vendor, OrgId = tenant, Name = $"Vendor {marker}", Active = true });
        await db.SaveChangesAsync();

        db.Donors.Add(new Donor { Id = donor, OrgId = tenant, Name = $"Ramen {marker}", Surname = "Das", ZoneId = zone });
        await db.SaveChangesAsync();

        db.Subscriptions.Add(new Subscription
        {
            Id = subscription, OrgId = tenant, DonorId = donor, YearId = year, Category = "member",
            Projected = collected, Billed = collected, Collected = collected, Mode = "Cash",
            CollectorId = collector, Comments = $"comment {marker}",
        });
        db.BudgetLines.Add(new BudgetLine { Id = budget, OrgId = tenant, YearId = year, Category = "Decoration", Notes = $"budget {marker}", Forecast = forecast, SortOrder = 1 });
        db.Expenses.Add(new Expense
        {
            Id = expense, OrgId = tenant, YearId = year, Category = "Decoration", Details = $"expense {marker}",
            Amount = spent, SpentOn = "2026-10-01", Mode = "Cash", VendorId = vendor,
        });
        db.Contracts.Add(new Contract
        {
            Id = contract, OrgId = tenant, YearId = year, VendorId = vendor, Title = $"Contract {marker}",
            Category = "Decoration", ContractAmount = 9000m, Status = "active",
        });
        db.CashLedgers.Add(new CashLedger { Id = cash, OrgId = tenant, YearId = year, Party = $"Party {marker}", Amount = 100m, EntryDate = "2026-10-02", Note = $"cash {marker}" });
        db.Advances.Add(new Advance
        {
            Id = adv, OrgId = tenant, YearId = year, IssuedToName = $"Advance holder {marker}", Amount = advance,
            IssuedOn = "2026-10-03", Purpose = $"purpose {marker}", Status = "open",
        });
        db.WorkflowItems.Add(new WorkflowItem
        {
            Id = workflow, OrgId = tenant, YearId = year, Title = $"Task {marker}", Status = "todo", Notes = $"workflow {marker}",
        });
        db.Documents.Add(new Document
        {
            Id = document, OrgId = tenant, YearId = year, Title = $"Doc {marker}", FileName = $"receipt-{marker}.pdf",
            BlobName = $"{tenant}/receipt-{marker}.pdf", ContentType = "application/pdf", SizeBytes = 12,
        });
        await db.SaveChangesAsync();

        // The blob the document points at, under the tenant's own prefix.
        Blobs.Put($"{tenant}/receipt-{marker}.pdf", $"PDF-BYTES-{marker}");

        return new TenantData(tenant, marker, year, zone, collector, donor, subscription, vendor, contract,
            expense, budget, cash, adv, workflow, document);
    }
}

[CollectionDefinition(nameof(DatabaseFixture))]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
