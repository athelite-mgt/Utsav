namespace Utsav.Domain.Tenancy;

/// <summary>
/// The committee whose data the current request is allowed to see.
///
/// Lives in Domain, with no dependency on ASP.NET or EF, so Infrastructure
/// and Api both depend inward on it rather than on each other.
///
/// There is deliberately no default and no nullable accessor that silently
/// yields <see cref="Guid.Empty"/>. In a pooled multi-tenant database an
/// unresolved tenant that degrades to "some default" is a cross-tenant data
/// leak, so the only two states are "resolved" and "throws".
/// </summary>
public interface ITenantContext
{
    /// <summary>The active committee. Throws <see cref="TenantNotResolvedException"/> if none.</summary>
    Guid TenantId { get; }

    /// <summary>True when a tenant has been established for this request.</summary>
    bool HasTenant { get; }
}

/// <summary>
/// Thrown when code reaches for the tenant on a request that never
/// established one: an anonymous request, or a signed-in user who has not
/// yet chosen a committee. Surfaces as 403, never as a query against an
/// arbitrary tenant.
/// </summary>
public sealed class TenantNotResolvedException()
    : InvalidOperationException(
        "No committee is active for this request. The caller must be signed in " +
        "with an active membership and a selected committee.");
