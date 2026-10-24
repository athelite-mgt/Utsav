using System.Collections.Frozen;

namespace Utsav.Domain.Authorization;

/// <summary>
/// The single authoritative statement of who can do what.
///
/// | Area                                   | Admin | Secretariat | Finance | Executive | Viewer |
/// |----------------------------------------|-------|-------------|---------|-----------|--------|
/// | Settings (lookups, years, rollover, …) |  CRUD |        read |    read |      read |   read |
/// | Users &amp; roles, access requests     |  CRUD |        CRUD |       – |         – |      – |
/// | Chanda, Ads, Special, Offerings        |  CRUD |        CRUD |    CRUD |      read |   read |
/// | Expenses, Budget, Vendors, Contracts   |  CRUD |        CRUD |    CRUD |      read |   read |
/// | Workflow / Tasks, Puja Schedule        |  CRUD |        CRUD |    CRUD |      CRUD |   read |
/// | Cash Advances (+ settlements)          |  CRUD |        CRUD |    CRUD |      CRUD |   read |
/// | Dashboard, Fund Position               |  read |        read |    read |      read |   read |
/// | Audit log                              |  read |        read |       – |         – |      – |
/// | Asura AI assistant (ask questions)     |   yes |         yes |     yes |         – |      – |
///
/// Reading the brief: everyone up to and including Executive writes Workflow,
/// Schedule and Cash Advances; Executive is otherwise read-everything; Viewer
/// is read-only throughout.
/// </summary>
public static class PermissionMatrix
{
    private static readonly FrozenDictionary<AppRole, FrozenSet<Permission>> Grants =
        new Dictionary<AppRole, FrozenSet<Permission>>
        {
            [AppRole.Admin] = FrozenSet.ToFrozenSet(
            [
                Permission.Read,
                Permission.ManageSettings,
                Permission.ManageUsers,
                Permission.WriteIncome,
                Permission.WriteFinance,
                Permission.WriteWorkflow,
                Permission.WriteAdvances,
                Permission.ReadAudit,
                Permission.UseAsura,
            ]),

            // Everything Admin has, minus committee configuration.
            [AppRole.Secretariat] = FrozenSet.ToFrozenSet(
            [
                Permission.Read,
                Permission.ManageUsers,
                Permission.WriteIncome,
                Permission.WriteFinance,
                Permission.WriteWorkflow,
                Permission.WriteAdvances,
                Permission.ReadAudit,
                Permission.UseAsura,
            ]),

            // The money, and the operational surfaces that go with it.
            [AppRole.Finance] = FrozenSet.ToFrozenSet(
            [
                Permission.Read,
                Permission.WriteIncome,
                Permission.WriteFinance,
                Permission.WriteWorkflow,
                Permission.WriteAdvances,
                Permission.UseAsura,
            ]),

            // Reads everything; writes only the operational surfaces.
            [AppRole.Executive] = FrozenSet.ToFrozenSet(
            [
                Permission.Read,
                Permission.WriteWorkflow,
                Permission.WriteAdvances,
            ]),

            [AppRole.Viewer] = FrozenSet.ToFrozenSet(
            [
                Permission.Read,
            ]),
        }.ToFrozenDictionary();

    public static bool Allows(AppRole role, Permission permission) =>
        Grants.TryGetValue(role, out var granted) && granted.Contains(permission);

    /// <summary>
    /// Everything a role may do — sent to the frontend on sign-in so the UI can
    /// hide controls the server would reject anyway. The server still checks
    /// every call; this only avoids showing buttons that would 403.
    /// </summary>
    public static IReadOnlyCollection<Permission> GrantedTo(AppRole role) =>
        Grants.TryGetValue(role, out var granted) ? granted : FrozenSet<Permission>.Empty;
}
