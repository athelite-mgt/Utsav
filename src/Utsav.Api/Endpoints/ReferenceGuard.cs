using System.Collections.Frozen;

namespace Utsav.Api.Endpoints;

/// <summary>
/// Thrown when a write names a record in another committee (or one that does
/// not exist) as something it belongs to. A 400, not a 403: the caller is
/// entitled to write, they just referred to something they cannot own.
/// </summary>
public sealed class ForeignReferenceException(string parameter)
    : InvalidOperationException($"'{parameter}' does not refer to a record in this committee.")
{
    public string Parameter { get; } = parameter;
}

/// <summary>
/// Which tenant table an id-valued parameter must point into.
///
/// SQL Server checks foreign keys without regard to row-level security, so the
/// database alone would let a record in one committee reference a row in
/// another. The ids are unguessable, but a cross-committee link is never
/// legitimate and would stop the other committee ever deleting the row it
/// points at. Checked centrally in the write executor, so no endpoint has to
/// remember to.
/// </summary>
public static class ReferenceGuard
{
    private static readonly FrozenDictionary<string, string> Tables = new Dictionary<string, string>
    {
        // Column names, as the generic CRUD routes bind them.
        ["donor_id"] = "donors",
        ["zone_id"] = "zones",
        ["year_id"] = "puja_years",
        ["collector_id"] = "collectors",
        ["owner_collector_id"] = "collectors",
        ["assignee_collector_id"] = "collectors",
        ["issued_to_collector_id"] = "collectors",
        ["vendor_id"] = "vendors",
        ["contract_id"] = "contracts",
        ["advance_id"] = "advances",
        ["advertiser_id"] = "advertisers",
        ["expense_id"] = "expenses",
        ["feast_id"] = "feasts",

        // Short names used by the hand-written statements. Only a Guid value
        // is checked, so @year carrying an int year number is left alone.
        ["year"] = "puja_years",
        ["vendor"] = "vendors",
        ["contract"] = "contracts",
        ["advance"] = "advances",
        ["collector"] = "collectors",
        ["donor"] = "donors",
        ["expense"] = "expenses",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string? TableFor(string parameterName) =>
        Tables.GetValueOrDefault(parameterName.TrimStart('@'));
}
