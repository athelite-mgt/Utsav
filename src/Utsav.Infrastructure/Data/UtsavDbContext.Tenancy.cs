using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Data;

/// <summary>
/// The request's tenant, carried by the request-scoped context.
///
/// The two raw-SQL chokepoints (reads and writes) take their <c>@org</c>
/// from here rather than from the caller. The context is scoped to the same
/// request as the tenant, so they cannot disagree, and no call site has to
/// remember to pass it.
/// </summary>
public partial class UtsavDbContext
{
    private readonly ITenantContext? _tenant;

    public UtsavDbContext(DbContextOptions<UtsavDbContext> options, ITenantContext tenant)
        : this(options) => _tenant = tenant;

    /// <summary>
    /// The active committee. Throws <see cref="TenantNotResolvedException"/>
    /// when there is none, including for a context built without a tenant at
    /// all: there is no default, so code that needs one fails loudly instead
    /// of querying an arbitrary committee.
    /// </summary>
    public Guid TenantId => _tenant?.TenantId ?? throw new TenantNotResolvedException();

    /// <summary>True when this context was created for a request with a resolved tenant.</summary>
    public bool HasTenant => _tenant is { HasTenant: true };

    /// <summary>
    /// Entities carrying <c>org_id</c> that must NOT be tenant-filtered, because
    /// they are read to establish or list tenancy: a session loads before any
    /// tenant exists, the committee switcher lists memberships across
    /// committees, and an invitation is redeemed by someone with no committee
    /// yet. Filtering either would make sign-in impossible. They are
    /// protected instead by the unguessable session token and by membership
    /// being re-verified on every session load.
    /// </summary>
    private static readonly HashSet<Type> UnfilteredByDesign = [typeof(Session), typeof(OrgMember), typeof(OrgInvite)];

    /// <summary>
    /// Guid.Empty when no tenant is resolved, which matches no row. A job or
    /// migration running without a request therefore sees nothing rather than
    /// everything: the safe direction to fail. Code that genuinely needs a
    /// cross-tenant read must say so with <c>IgnoreQueryFilters()</c>, which
    /// is greppable.
    /// </summary>
    private Guid FilterTenantId => _tenant is { HasTenant: true } ? _tenant.TenantId : Guid.Empty;

    /// <summary>
    /// Tenant filtering for the LINQ query paths. Applied by reflection over
    /// every entity with an <c>OrgId</c> property rather than ~35 hand-written
    /// lines, so adding an org-scoped entity gets filtered automatically
    /// instead of needing a developer to remember. This is layer three of
    /// four: the raw-SQL endpoints never go through the EF query pipeline,
    /// which is what the chokepoint guard covers, and row-level security
    /// covers anything that evades both.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var orgId = entityType.FindProperty("OrgId");
            if (orgId is null || UnfilteredByDesign.Contains(entityType.ClrType))
            {
                continue;
            }

            var method = orgId.ClrType == typeof(Guid?) ? nameof(SetNullableTenantFilter) : nameof(SetTenantFilter);
            typeof(UtsavDbContext)
                .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    // FilterTenantId is read through the context instance, so EF evaluates it
    // per query rather than baking the first request's tenant into the cached
    // model - which would be a cross-tenant leak.
    private void SetTenantFilter<T>(ModelBuilder modelBuilder) where T : class =>
        modelBuilder.Entity<T>().HasQueryFilter(e => EF.Property<Guid>(e, "OrgId") == FilterTenantId);

    private void SetNullableTenantFilter<T>(ModelBuilder modelBuilder) where T : class =>
        modelBuilder.Entity<T>().HasQueryFilter(e => EF.Property<Guid?>(e, "OrgId") == FilterTenantId);
}
