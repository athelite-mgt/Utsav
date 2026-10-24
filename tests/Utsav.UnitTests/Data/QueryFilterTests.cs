using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using Utsav.Infrastructure.Tenancy;
using Xunit;

namespace Utsav.UnitTests.Data;

public class QueryFilterTests
{
    private static UtsavDbContext For(Guid tenant, string store)
    {
        var context = new TenantContext();
        context.Resolve(tenant);
        return new UtsavDbContext(
            new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(store).Options,
            context);
    }

    private static UtsavDbContext Unresolved(string store) =>
        new(new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(store).Options, new TenantContext());

    private static Document Doc(Guid org, string name) => new()
    {
        Id = Guid.NewGuid(), OrgId = org, Title = name, FileName = name, BlobName = $"{org}/{name}",
        ContentType = "application/pdf", SizeBytes = 1,
    };

    [Fact]
    public async Task Linq_reads_see_only_the_active_tenants_rows()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        const string store = nameof(Linq_reads_see_only_the_active_tenants_rows);

        await using (var seed = For(orgA, store))
        {
            seed.Documents.AddRange(Doc(orgA, "a.pdf"), Doc(orgB, "b.pdf"));
            await seed.SaveChangesAsync();
        }

        await using var asA = For(orgA, store);
        var visible = await asA.Documents.ToListAsync();

        Assert.Single(visible);
        Assert.Equal("a.pdf", visible[0].FileName);
    }

    [Fact]
    public async Task A_row_belonging_to_another_tenant_is_not_findable_by_id()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var foreignId = Guid.NewGuid();
        const string store = nameof(A_row_belonging_to_another_tenant_is_not_findable_by_id);

        await using (var seed = For(orgB, store))
        {
            seed.ApiKeys.Add(new ApiKey { Id = foreignId, OrgId = orgB, Name = "theirs", KeyHash = "h", Prefix = "p" });
            await seed.SaveChangesAsync();
        }

        await using var asA = For(orgA, store);

        // Guessing an id must not be enough: this is the shape that turns an
        // IDOR into a cross-committee breach rather than a nuisance.
        Assert.Null(await asA.ApiKeys.SingleOrDefaultAsync(k => k.Id == foreignId));
    }

    [Fact]
    public async Task A_context_with_no_resolved_tenant_sees_no_tenant_rows()
    {
        // A job or migration running outside a request must see nothing
        // rather than everything: the safe direction to fail.
        var orgA = Guid.NewGuid();
        const string store = nameof(A_context_with_no_resolved_tenant_sees_no_tenant_rows);

        await using (var seed = For(orgA, store))
        {
            seed.Documents.Add(Doc(orgA, "a.pdf"));
            await seed.SaveChangesAsync();
        }

        await using var nobody = Unresolved(store);
        Assert.Empty(await nobody.Documents.ToListAsync());
    }

    [Fact]
    public async Task Global_tables_are_not_filtered()
    {
        const string store = nameof(Global_tables_are_not_filtered);

        await using (var seed = For(Guid.NewGuid(), store))
        {
            seed.Users.Add(new User { Id = Guid.NewGuid(), Email = "x@y.z", AuthProvider = "local", PasswordHash = "h", Role = "viewer" });
            await seed.SaveChangesAsync();
        }

        await using var asOther = For(Guid.NewGuid(), store);

        // users is global identity: filtering it would break sign-in for
        // everyone outside the active tenant.
        Assert.Single(await asOther.Users.ToListAsync());
    }

    [Fact]
    public async Task Sessions_and_memberships_are_deliberately_not_filtered()
    {
        // Both are read to ESTABLISH or LIST tenancy: a session must load
        // before any tenant exists, and the committee switcher must see
        // memberships across committees. Filtering either breaks sign-in.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var user = Guid.NewGuid();
        const string store = nameof(Sessions_and_memberships_are_deliberately_not_filtered);

        await using (var seed = For(orgA, store))
        {
            seed.OrgMembers.AddRange(
                new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = user, Role = "admin", Status = "active" },
                new OrgMember { Id = Guid.NewGuid(), OrgId = orgB, UserId = user, Role = "viewer", Status = "active" });
            seed.Sessions.Add(new Session { Token = "tok", UserId = user, OrgId = orgB });
            await seed.SaveChangesAsync();
        }

        await using var asA = For(orgA, store);
        Assert.Equal(2, await asA.OrgMembers.CountAsync());
        Assert.Single(await asA.Sessions.ToListAsync());
    }

    [Fact]
    public async Task IgnoreQueryFilters_is_the_explicit_escape_hatch()
    {
        // Code that genuinely needs a cross-tenant read (API-key lookup before
        // any tenant is known) must say so, and it must be greppable.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        const string store = nameof(IgnoreQueryFilters_is_the_explicit_escape_hatch);

        await using (var seed = For(orgB, store))
        {
            seed.ApiKeys.Add(new ApiKey { Id = Guid.NewGuid(), OrgId = orgB, Name = "k", KeyHash = "hash-1", Prefix = "p" });
            await seed.SaveChangesAsync();
        }

        await using var nobody = Unresolved(store);
        Assert.Null(await nobody.ApiKeys.SingleOrDefaultAsync(k => k.KeyHash == "hash-1"));
        Assert.NotNull(await nobody.ApiKeys.IgnoreQueryFilters().SingleOrDefaultAsync(k => k.KeyHash == "hash-1"));
    }

    private static UtsavDbContext SqlServerFor(Guid tenant)
    {
        var context = new TenantContext();
        context.Resolve(tenant);
        return new UtsavDbContext(
            new DbContextOptionsBuilder<UtsavDbContext>().UseSqlServer("Server=translation-only;Database=none;").Options,
            context);
    }

    [Fact]
    public void The_filter_translates_to_sql_and_binds_each_contexts_own_tenant()
    {
        // The model is built once and cached for the process lifetime, while the
        // tenant changes per request. If the filter baked the first tenant into
        // the cached model, every later request would query the FIRST
        // committee's data. Two contexts, two tenants, same model: each must
        // carry its own tenant in the generated SQL.
        var orgA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var orgB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        string sqlA, sqlB, nullableSql;
        using (var a = SqlServerFor(orgA))
        {
            sqlA = a.Donors.ToQueryString();
            nullableSql = a.ApiKeys.ToQueryString();
        }

        using (var b = SqlServerFor(orgB))
        {
            sqlB = b.Donors.ToQueryString();
        }

        Assert.Contains("org_id", sqlA);
        Assert.Contains(orgA.ToString(), sqlA, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(orgB.ToString(), sqlA, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(orgB.ToString(), sqlB, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(orgA.ToString(), sqlB, StringComparison.OrdinalIgnoreCase);

        // The nullable org_id shape (api_keys) translates too.
        Assert.Contains("org_id", nullableSql);
    }
}
