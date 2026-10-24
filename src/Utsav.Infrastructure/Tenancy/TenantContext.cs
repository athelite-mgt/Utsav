using Utsav.Domain.Tenancy;

namespace Utsav.Infrastructure.Tenancy;

/// <summary>
/// Scoped per request. Populated once by the authentication handler, then
/// read by the SQL chokepoints, the EF query filters and the RLS connection
/// interceptor.
///
/// Write-once by design: a request's tenant is fixed the moment
/// authentication resolves it. Switching committees is a separate,
/// authenticated round trip that updates the session row, not a mutation of
/// an in-flight request.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    private Guid? _tenantId;

    public bool HasTenant => _tenantId.HasValue;

    public Guid TenantId => _tenantId ?? throw new TenantNotResolvedException();

    public void Resolve(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id must not be empty.", nameof(tenantId));
        }

        if (_tenantId.HasValue)
        {
            throw new InvalidOperationException(
                $"Tenant is already resolved to {_tenantId}. A request's tenant cannot change.");
        }

        _tenantId = tenantId;
    }
}
