using Microsoft.EntityFrameworkCore;
using Utsav.Domain.Tenancy;

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
}
