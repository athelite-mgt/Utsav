namespace Utsav.Infrastructure.Audit;

/// <summary>
/// One row of the audit trail — who did what to which table, when, from
/// where, and what the data looked like before/after. TASKLIST §5.1.
///
/// <see cref="Id"/> and <see cref="OrgId"/> are the Cosmos item id and
/// partition key (<c>/orgId</c> — TASKLIST §5.3); every other property name
/// is written camelCase (see <c>CosmosClientOptions.SerializerOptions</c> in
/// <c>DependencyInjection.AddUtsavInfrastructure</c>) so the wire shape
/// matches that partition key path exactly.
/// </summary>
public sealed record AuditEntry(
    string Id,
    Guid OrgId,
    DateTimeOffset Timestamp,
    Guid? UserId,
    string? UserEmail,
    string? UserFullName,
    string? UserRole,
    string Action,
    string EntityType,
    /// <summary>
    /// A human name for the specific record touched — the donor's name, the
    /// expense details, the vendor's title. Resolved at write time (see
    /// <see cref="AuditableTables"/>) so History can identify the row even
    /// when only an amount changed and the name itself wasn't part of the edit.
    /// </summary>
    string? EntityLabel,
    string? RequestMethod,
    string? RequestPath,
    string CorrelationId,
    string? IpAddress,
    string? UserAgent,
    string Outcome,
    string? ErrorMessage,
    IReadOnlyList<IReadOnlyDictionary<string, object?>>? Before,
    IReadOnlyDictionary<string, object?>? After)
{
    public const string Success = "Success";
    public const string Failure = "Failure";
}
