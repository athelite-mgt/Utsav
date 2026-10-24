using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Utsav.Domain.Tenancy;

namespace Utsav.Infrastructure.Tenancy;

/// <summary>
/// Stamps the active committee onto every physical database connection, which
/// is what the row-level security predicate reads.
///
/// This must run on connection OPEN, not once per DbContext: connections are
/// pooled, and a connection handed back to the pool and reissued to another
/// committee's request would otherwise still carry the previous tenant's
/// session context - a cross-tenant read with no bug anywhere in application
/// code. (SQL Server also resets session context when a pooled connection is
/// reused; setting it again on every open makes the correct value explicit
/// rather than depending on that.)
///
/// @read_only = 1 means the value cannot be changed again for the life of the
/// connection, so nothing downstream - a stored procedure, an injected
/// statement - can re-point it at another committee.
/// </summary>
public sealed class TenantSessionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken ct)
    {
        if (!tenant.HasTenant)
        {
            // No tenant: leave the context unset. The predicate then matches
            // nothing, so an unscoped connection reads no rows rather than
            // all of them.
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sp_set_session_context @key = N'TenantId', @value = @tenant, @read_only = 1;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@tenant";
        parameter.Value = tenant.TenantId;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(ct);
    }
}
