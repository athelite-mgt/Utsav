namespace Utsav.Infrastructure.Data.Migrations;

/// <summary>
/// The fourth and last isolation layer: the database itself refuses to return
/// another committee's rows, regardless of what the application asked for.
///
/// The first three layers (the two SQL chokepoint guards and the EF query
/// filters) are application code and can be bypassed by a bug. This one
/// cannot, which is why it exists even though the layers above it should
/// already be sufficient. A missed predicate becomes "no rows returned"
/// instead of "another committee's donor list".
///
/// FILTER controls what reads, updates and deletes can see; BLOCK AFTER
/// INSERT/UPDATE stops a row being written into, or moved to, a tenant other
/// than the active one, so a forged org_id cannot plant data in someone
/// else's committee.
///
/// SCHEMABINDING on the predicate is required by SQL Server for a security
/// policy and has a useful side effect: a column rename that would break the
/// predicate fails at migration time rather than silently disabling scoping.
/// </summary>
internal static class RowLevelSecurity
{
    /// <summary>
    /// Every table carrying org_id EXCEPT three, which are read before any
    /// tenant is known and so cannot be tenant-scoped:
    ///   sessions     - loaded by an unguessable token to establish the tenant;
    ///   org_members  - listed across committees for the switcher;
    ///   api_keys     - looked up by a high-entropy hash, which is what
    ///                  establishes the tenant.
    /// Each is protected instead by its own secret and, for memberships, by
    /// being re-verified on every session load.
    /// </summary>
    internal static readonly string[] ProtectedTables =
    [
        "ad_bookings", "advance_settlements", "advances", "advertisers",
        "budget_lines", "cash_ledger", "collection_payments", "collectors",
        "contract_payments", "contracts", "documents", "donors", "expenses",
        "feast_expenses", "feast_tokens", "feasts", "fund_transfers", "lookups",
        "notifications", "offerings", "org_role_mappings", "puja_calendar_days",
        "puja_schedule", "puja_years", "push_subscriptions", "special_donations",
        "subscriptions", "vendor_issues", "vendors", "workflow_items",
        "workflow_notes", "zones",
    ];

    /// <summary>
    /// The least-privileged role the application connects through. It is NOT
    /// db_owner and holds no ALTER ANY SECURITY POLICY, so the app cannot
    /// disable the policy it runs under even if compromised. The deploy role
    /// is the one principal the predicate lets past, for migrations and bulk
    /// import; the application's login is never a member of it.
    /// </summary>
    public const string CreateRoles = """
        IF DATABASE_PRINCIPAL_ID(N'utsav_app_role') IS NULL
            CREATE ROLE utsav_app_role;
        IF DATABASE_PRINCIPAL_ID(N'utsav_deploy_role') IS NULL
            CREATE ROLE utsav_deploy_role;
        ALTER ROLE db_datareader ADD MEMBER utsav_app_role;
        ALTER ROLE db_datawriter ADD MEMBER utsav_app_role;
        """;

    public const string CreatePredicate = """
        CREATE FUNCTION dbo.fn_tenant_predicate(@org_id uniqueidentifier)
            RETURNS TABLE
            WITH SCHEMABINDING
        AS
            RETURN SELECT 1 AS allowed
            WHERE @org_id = CAST(SESSION_CONTEXT(N'TenantId') AS uniqueidentifier)
               OR IS_ROLEMEMBER(N'utsav_deploy_role') = 1;
        """;

    public static string CreatePolicy =>
        "CREATE SECURITY POLICY dbo.TenantIsolationPolicy\n" +
        string.Join(",\n", ProtectedTables.Select(t =>
            $"  ADD FILTER PREDICATE dbo.fn_tenant_predicate(org_id) ON dbo.{t},\n" +
            $"  ADD BLOCK PREDICATE dbo.fn_tenant_predicate(org_id) ON dbo.{t} AFTER INSERT,\n" +
            $"  ADD BLOCK PREDICATE dbo.fn_tenant_predicate(org_id) ON dbo.{t} AFTER UPDATE")) +
        "\nWITH (STATE = ON);";

    public static string[] CreateAll => [CreateRoles, CreatePredicate, CreatePolicy];

    public static string[] DropAll =>
    [
        "DROP SECURITY POLICY IF EXISTS dbo.TenantIsolationPolicy;",
        "DROP FUNCTION IF EXISTS dbo.fn_tenant_predicate;",
    ];
}
