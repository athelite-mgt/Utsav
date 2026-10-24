using System.Collections.Frozen;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Utsav.Infrastructure.Audit;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Push;

namespace Utsav.Api.Endpoints;

/// <summary>
/// The single place this codebase is allowed to run SQL whose text was
/// assembled at runtime.
///
/// EF1002 flags <c>ExecuteSqlRawAsync</c> whenever the statement is built by
/// interpolation, because that is normally how injection gets in. These
/// endpoints genuinely need it: SQL cannot parameterise a table or column name,
/// and the generic CRUD routes are addressed by table. The rule that makes it
/// safe is that no interpolated fragment ever originates from the request —
/// identifiers are resolved to string instances declared in
/// <see cref="TableRegistry"/> (see <see cref="TableRegistry.TryResolve"/>),
/// and everything a caller actually supplies is bound as a parameter.
///
/// Routing every such call through here keeps that suppression in one
/// reviewable spot instead of scattered across the endpoint files, so a future
/// <c>ExecuteSqlRawAsync</c> written somewhere else still trips the analyzer
/// and gets looked at.
///
/// Being the one chokepoint for every mutation (27 call sites, no exceptions)
/// also makes it the one place that can audit every one of them automatically
/// — TASKLIST §5. Every write endpoint already threads a <see cref="UtsavDbContext"/>
/// through explicitly rather than taking a DI-injected instance of this
/// static class, so audit capture needs the ambient request (who/where) a
/// different way: <see cref="Configure"/> is called once from Program.cs
/// after the DI container is built, the same pattern <see cref="IHttpContextAccessor"/>
/// is itself designed to be used for — safe because both dependencies are
/// singletons for the app's lifetime.
/// </summary>
internal static class DynamicSql
{
    // First non-whitespace token(s) of every statement here is always the
    // verb + table — confirmed across all 27 call sites, both the
    // hand-written templates ("UPDATE org_members SET ...") and the
    // generic-CRUD ones built with bracketed identifiers ("UPDATE [expenses] SET ...").
    private static readonly Regex VerbAndTable = new(
        @"^\s*(?<verb>INSERT INTO|UPDATE|DELETE FROM)\s+\[?(?<table>\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static IHttpContextAccessor? _httpContextAccessor;
    private static IAuditSink? _auditSink;
    private static IPushDispatcher? _pushDispatcher;

    /// <summary>
    /// No-ops (audit capture and push notifications silently disabled) until
    /// this is called — keeps unit tests that exercise a
    /// <see cref="UtsavDbContext"/> directly, without going through the real
    /// Program.cs startup, working unchanged.
    /// </summary>
    public static void Configure(IHttpContextAccessor httpContextAccessor, IAuditSink auditSink, IPushDispatcher pushDispatcher)
    {
        _httpContextAccessor = httpContextAccessor;
        _auditSink = auditSink;
        _pushDispatcher = pushDispatcher;
    }

#pragma warning disable EF1002 // Justified above: identifiers come from TableRegistry, values are parameters.
    public static async Task<int> ExecuteAsync(
        UtsavDbContext db, string sql, IEnumerable<object> parameters, CancellationToken ct)
    {
        var parameterList = parameters as IReadOnlyCollection<object> ?? parameters.ToList();
        var (verb, table) = ParseVerbAndTable(sql);

        // Must run before the mutation — it's the only chance to see what the
        // row(s) looked like beforehand. Best-effort: a diagnostic snapshot
        // must never be why a real save fails.
        var before = table is not null && (verb == "UPDATE" || verb == "DELETE FROM")
            ? await TryCaptureBeforeAsync(db, table, sql, parameterList, ct)
            : null;

        try
        {
            var rowsAffected = await db.Database.ExecuteSqlRawAsync(sql, parameterList, ct);

            // Resolved once, from the untrimmed pre-image (update/delete) or the
            // written values (insert), so History can name the record even when
            // only an amount changed. Best-effort — a null label is fine.
            var label = await TryResolveEntityLabelAsync(db, table, before, parameterList, ct);

            await TryAuditAsync(db, table, verb, AuditEntry.Success, null, label, before, parameterList, ct);
            // Failed mutations never reach here (the catch below runs instead)
            // — nobody needs paging about a save that didn't happen.
            await TryNotifyAsync(table, verb, label, before, parameterList, ct);
            return rowsAffected;
        }
        catch (Exception ex)
        {
            // TASKLIST §5.8 — audited even when it fails, then rethrown so
            // normal error handling is completely unchanged.
            await TryAuditAsync(db, table, verb, AuditEntry.Failure, ex.Message, null, before, parameterList, ct);
            throw;
        }
    }
#pragma warning restore EF1002

    /// <summary>
    /// Audits one settlement (collection_payments) change. PersistCollectionPaymentsAsync
    /// replaces a collection's whole instalment set with EF Core directly rather than
    /// issuing per-row SQL through <see cref="ExecuteAsync"/>, so those writes never ride
    /// the audit capture built into that path — this is the dedicated equivalent, called
    /// once per added/removed instalment so each settlement shows up in History with its
    /// own amount/date/mode, the same granularity as any other write. Best-effort, same
    /// contract as <see cref="TryAuditAsync"/>: never why a real save fails.
    /// </summary>
    public static async Task AuditCollectionPaymentAsync(
        string verb, string? entityLabel, Dictionary<string, object?> fields, CancellationToken ct)
    {
        if (_auditSink is null)
        {
            return;
        }

        try
        {
            var before = verb == "DELETE FROM" ? new List<Dictionary<string, object?>> { fields } : null;
            var after = verb == "DELETE FROM" ? new Dictionary<string, object?>() : fields;
            var entry = BuildEntry("collection_payments", verb, AuditEntry.Success, null, entityLabel, before, after, _httpContextAccessor?.HttpContext);
            await _auditSink.WriteAsync(entry, ct);
        }
        catch
        {
            // Audit capture must never be why a real mutation fails.
        }
    }

    /// <summary>
    /// Audits one feast-token print run as a single entry, not one per token.
    /// FeastTokenEndpoints inserts the whole batch with EF directly (so it can
    /// read back the max seq_no and allocate the run's codes inside one
    /// transaction), which bypasses the per-statement capture built into
    /// <see cref="ExecuteAsync"/> — this is the dedicated equivalent, called
    /// once per print request so History shows "Printed 50 tokens (F2026-0001
    /// – F2026-0050)" rather than fifty indistinguishable blank-token rows.
    /// Best-effort, same contract as <see cref="TryAuditAsync"/>.
    /// </summary>
    public static async Task AuditFeastTokenPrintAsync(
        string entityLabel, Dictionary<string, object?> fields, CancellationToken ct)
    {
        if (_auditSink is null)
        {
            return;
        }

        try
        {
            var entry = BuildEntry("feast_tokens", "INSERT INTO", AuditEntry.Success, null, entityLabel, null, fields, _httpContextAccessor?.HttpContext);
            await _auditSink.WriteAsync(entry, ct);
        }
        catch
        {
            // Audit capture must never be why a real mutation fails.
        }
    }

    /// <summary>Pure and side-effect-free so it's directly unit-testable against real SQL text from every call site.</summary>
    internal static (string? Verb, string? Table) ParseVerbAndTable(string sql)
    {
        var match = VerbAndTable.Match(sql);
        return match.Success
            ? (match.Groups["verb"].Value.ToUpperInvariant(), match.Groups["table"].Value)
            : (null, null);
    }

    /// <summary>
    /// The "after" half of the audit entry — the values actually being
    /// written. Pure: <see cref="SqlParameter"/> objects in, a plain
    /// dictionary out, <c>DBNull</c> normalised to <c>null</c> the same way
    /// <see cref="CrudEndpoints"/> already does when reading a row back.
    /// </summary>
    internal static Dictionary<string, object?> ExtractParameters(IEnumerable<object> parameters)
    {
        var result = new Dictionary<string, object?>();
        foreach (var parameter in parameters.OfType<SqlParameter>())
        {
            result[parameter.ParameterName.TrimStart('@')] = parameter.Value is DBNull ? null : parameter.Value;
        }

        return result;
    }

    /// <summary>
    /// Assembles the full entry from already-known facts plus whatever the
    /// ambient <see cref="HttpContext"/> has — pure given its inputs
    /// (including <paramref name="httpContext"/>, which in production is
    /// whatever <see cref="IHttpContextAccessor"/> currently holds but in a
    /// test is just a plain constructible <c>DefaultHttpContext</c>).
    /// </summary>
    internal static AuditEntry BuildEntry(
        string table, string verb, string outcome, string? errorMessage, string? entityLabel,
        List<Dictionary<string, object?>>? before, Dictionary<string, object?> after,
        HttpContext? httpContext)
    {
        var user = httpContext?.User;

        return new AuditEntry(
            Id: Guid.NewGuid().ToString(),
            OrgId: SessionService.DefaultOrgId,
            Timestamp: DateTimeOffset.UtcNow,
            UserId: Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : null,
            UserEmail: user?.FindFirst(ClaimTypes.Email)?.Value,
            UserFullName: user?.FindFirst(ClaimTypes.Name)?.Value,
            UserRole: user?.FindFirst(ClaimTypes.Role)?.Value,
            Action: verb == "DELETE FROM" ? "DELETE" : verb,
            EntityType: table,
            EntityLabel: entityLabel,
            RequestMethod: httpContext?.Request.Method,
            RequestPath: httpContext?.Request.Path.Value,
            CorrelationId: httpContext?.TraceIdentifier ?? Guid.NewGuid().ToString(),
            IpAddress: httpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent: httpContext?.Request.Headers.UserAgent.ToString(),
            Outcome: outcome,
            ErrorMessage: errorMessage,
            Before: before,
            After: after);
    }

    private static async Task<List<Dictionary<string, object?>>?> TryCaptureBeforeAsync(
        UtsavDbContext db, string table, string sql, IReadOnlyCollection<object> parameters, CancellationToken ct)
    {
        var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        if (whereIndex < 0)
        {
            return null;
        }

        try
        {
            // Same predicate text, same parameter values — reused as a SELECT
            // rather than parsed, so this works regardless of what the
            // predicate actually says. Fresh SqlParameter instances: ADO.NET
            // parameter objects can't be added to more than one command, and
            // the originals still need to reach the real mutation untouched.
            var whereClause = sql[whereIndex..].TrimEnd(';', ' ', '\n', '\r', '\t');
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await db.Database.OpenConnectionAsync(ct);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM [{table}] {whereClause}";
            // Must enlist in the ambient transaction when one is active (e.g.
            // rollover's manually-managed BeginTransactionAsync) — a raw
            // SqlCommand off the connection doesn't do this automatically, and
            // SqlClient refuses to run it otherwise. Confirmed against a real
            // production failure in the sibling ModuleEndpoints.QueryAsync,
            // which had the exact same gap — see its remarks.
            command.Transaction = (SqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var original in parameters.OfType<SqlParameter>())
            {
                command.Parameters.Add(new SqlParameter(original.ParameterName, original.Value ?? DBNull.Value));
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>(reader.FieldCount);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
                }

                rows.Add(row);
            }

            return rows;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A handful of columns carry a foreign-key GUID rather than a value a
    /// person would recognise — "which zone", "which collector", "which
    /// vendor" — for tables/edits that don't otherwise resolve to a friendly
    /// label the way <see cref="TryResolveEntityLabelAsync"/> does for the
    /// record itself. Resolved here, once, so History shows "North Zone →
    /// South Zone" instead of two GUIDs a reader can't tell apart.
    /// </summary>
    private static readonly FrozenDictionary<string, (string FkTable, string FkNameColumn)> FriendlyFkFields =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["zone_id"] = ("zones", "name"),
            ["collector_id"] = ("collectors", "name"),
            ["assignee_collector_id"] = ("collectors", "name"),
            ["owner_collector_id"] = ("collectors", "name"),
            ["issued_to_collector_id"] = ("collectors", "name"),
            ["vendor_id"] = ("vendors", "name"),
            ["contract_id"] = ("contracts", "title"),
        }.ToFrozenDictionary();

    private static async Task TryAuditAsync(
        UtsavDbContext db, string? table, string? verb, string outcome, string? errorMessage, string? entityLabel,
        List<Dictionary<string, object?>>? before, IReadOnlyCollection<object> parameters, CancellationToken ct)
    {
        if (_auditSink is null || table is null || verb is null)
        {
            return;
        }

        try
        {
            var after = ExtractParameters(parameters);
            // UPDATE statements re-send every column the endpoint knows about,
            // not just the ones a user actually touched, so the raw before/after
            // pair duplicates every unchanged value on every save. Trimmed down
            // to only what changed — same information History.tsx already
            // derives client-side, just not re-sent and re-stored in full each time.
            var (auditBefore, auditAfter) = verb == "UPDATE" ? DiffChangedFields(before, after) : (before, after);
            await ResolveFriendlyFkValuesAsync(db, auditBefore, auditAfter, ct);
            var entry = BuildEntry(table, verb, outcome, errorMessage, entityLabel, auditBefore, auditAfter, _httpContextAccessor?.HttpContext);
            await _auditSink.WriteAsync(entry, ct);
        }
        catch
        {
            // Audit capture must never be why a real mutation fails.
            // QueuedAuditSink logs the one expected failure mode (queue
            // full) itself; anything reaching here is unexpected and, by
            // the same rule this whole file exists to enforce, still not a
            // reason to break the caller's actual save.
        }
    }

    /// <summary>
    /// A push for "any transaction update" — money-movement tables only
    /// (<see cref="NotifiableTables"/>), fanned out to every active
    /// non-viewer member except whoever made the change. Same best-effort
    /// contract as <see cref="TryAuditAsync"/>: this can never be why a real
    /// mutation fails, and it only runs after the mutation already
    /// succeeded (see the one call site in <see cref="ExecuteAsync"/>).
    /// </summary>
    private const string NotifiedKey = "puja:push-notified";

    private static async Task TryNotifyAsync(
        string? table, string? verb, string? entityLabel,
        List<Dictionary<string, object?>>? before, IReadOnlyCollection<object> parameters, CancellationToken ct)
    {
        if (_pushDispatcher is null || table is null || verb is null)
        {
            return;
        }

        if (!NotifiableTables.Meta.TryGetValue(table, out var meta))
        {
            return;
        }

        var http = _httpContextAccessor?.HttpContext;
        if (http is not null)
        {
            // At most one push per HTTP request. A single user action can write
            // several rows (deleting a contract also nulls its expenses'
            // contract_id; settling an advance writes both a settlement and an
            // expense) — without this each becomes its own buzz.
            if (http.Items.ContainsKey(NotifiedKey))
            {
                return;
            }

            // And of those writes, only the one that IS what the request was
            // about should notify — not a cascade side-effect. The request
            // path names the primary resource (/api/contracts/{id} → contracts),
            // so a notifiable write to some *other* notifiable table during a
            // contracts request is a side-effect and stays silent.
            var resource = PrimaryResource(http.Request.Path.Value);
            if (resource is not null && resource != table && NotifiableTables.Meta.ContainsKey(resource))
            {
                return;
            }

            http.Items[NotifiedKey] = true;
        }

        try
        {
            var user = http?.User;
            var actorName = user?.FindFirst(ClaimTypes.Name)?.Value
                ?? user?.FindFirst(ClaimTypes.Email)?.Value
                ?? "Someone";
            var actorUserId = Guid.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)
                ? uid
                : (Guid?)null;

            var verbWord = verb switch
            {
                "INSERT INTO" => "added",
                "UPDATE" => "updated",
                "DELETE FROM" => "deleted",
                _ => "changed",
            };

            // A delete's own parameters are just @id/@org — no money field —
            // so the amount for a deleted row comes from its captured
            // "before" image instead.
            var after = ExtractParameters(parameters);
            var amount = ExtractAmount(after) ?? ExtractAmount(before is { Count: 1 } ? before[0] : null);

            var record = string.IsNullOrWhiteSpace(entityLabel) ? null : entityLabel;
            var title = $"{meta.Label} {verbWord}";
            var detail = record is not null ? $" · {record}" : "";
            var body = amount is { } a ? $"{actorName}{detail} — ₹{a:N0}" : $"{actorName}{detail}";

            await _pushDispatcher.EnqueueAsync(
                new PushNotificationRequest(SessionService.DefaultOrgId, actorUserId, title, body, meta.Route,
                    ActorName: actorName, Kind: "transaction"), ct);
        }
        catch
        {
            // Same rule as audit capture: never why a real mutation fails.
        }
    }

    /// <summary>The resource segment of an API path — "contracts" from "/api/contracts/{id}" — or null if it isn't an /api/ route.</summary>
    internal static string? PrimaryResource(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // segments[0] is "api"; segments[1] is the resource.
        return segments.Length >= 2 && segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            ? segments[1].ToLowerInvariant()
            : null;
    }

    /// <summary>
    /// Mutates <paramref name="after"/> (and the single row of <paramref name="before"/>,
    /// if present) in place, replacing any <see cref="FriendlyFkFields"/> GUID
    /// value with the row it points at's name. Best-effort per field — a
    /// lookup failure or a deleted row just leaves the raw GUID rather than
    /// losing the audit entry.
    /// </summary>
    private static async Task ResolveFriendlyFkValuesAsync(
        UtsavDbContext db, List<Dictionary<string, object?>>? before, Dictionary<string, object?> after, CancellationToken ct)
    {
        var keys = new HashSet<string>(after.Keys, StringComparer.Ordinal);
        if (before is { Count: 1 })
        {
            foreach (var k in before[0].Keys)
            {
                keys.Add(k);
            }
        }

        foreach (var key in keys)
        {
            if (!FriendlyFkFields.TryGetValue(key, out var fk))
            {
                continue;
            }

            if (after.TryGetValue(key, out var av))
            {
                after[key] = await ResolveOneAsync(db, fk, av, ct);
            }

            if (before is { Count: 1 })
            {
                var singleBefore = before[0];
                if (singleBefore.TryGetValue(key, out var bv))
                {
                    singleBefore[key] = await ResolveOneAsync(db, fk, bv, ct);
                }
            }
        }
    }

    private static async Task<object?> ResolveOneAsync(
        UtsavDbContext db, (string FkTable, string FkNameColumn) fk, object? raw, CancellationToken ct)
    {
        if (raw is null)
        {
            return null;
        }

        if (!Guid.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var id))
        {
            return raw;
        }

        try
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await db.Database.OpenConnectionAsync(ct);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT TOP 1 [{fk.FkNameColumn}] FROM [{fk.FkTable}] WHERE id = @id";
            command.Transaction = (SqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new SqlParameter("@id", id));
            var result = await command.ExecuteScalarAsync(ct);
            return result is null or DBNull ? raw : Convert.ToString(result, CultureInfo.InvariantCulture);
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>
    /// A human name for the touched record — from the untrimmed pre-image
    /// (update/delete) or the written values (insert), with a one-row FK lookup
    /// for tables whose name lives elsewhere (subscriptions→donors,
    /// ad_bookings→advertisers). Best-effort: any failure yields null and
    /// History just shows the page without a record name.
    /// </summary>
    private static async Task<string?> TryResolveEntityLabelAsync(
        UtsavDbContext db, string? table, List<Dictionary<string, object?>>? before,
        IReadOnlyCollection<object> parameters, CancellationToken ct)
    {
        if (table is null || !AuditableTables.Map.TryGetValue(table, out var meta))
        {
            return null;
        }

        try
        {
            var row = before is { Count: 1 } ? before[0] : ExtractParameters(parameters);

            foreach (var column in meta.NameColumns)
            {
                if (row.TryGetValue(column, out var value) && value is not null)
                {
                    var text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            if (meta.FkTable is not null && meta.FkIdColumn is not null && meta.FkNameColumn is not null
                && row.TryGetValue(meta.FkIdColumn, out var fkId) && fkId is not null)
            {
                var connection = db.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                {
                    await db.Database.OpenConnectionAsync(ct);
                }

                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT TOP 1 [{meta.FkNameColumn}] FROM [{meta.FkTable}] WHERE id = @fkId";
                command.Transaction = (SqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
                command.Parameters.Add(new SqlParameter("@fkId", fkId));
                var result = await command.ExecuteScalarAsync(ct);
                return result is null or DBNull ? null : Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The first non-zero amount-shaped field found, checked in this order
    /// because "money that changed hands" is more newsworthy than "money
    /// that was only asked for" — collected before billed before projected.
    /// </summary>
    private static decimal? ExtractAmount(Dictionary<string, object?>? data)
    {
        if (data is null)
        {
            return null;
        }

        foreach (var key in new[] { "collected", "amount", "billed", "projected" })
        {
            if (data.TryGetValue(key, out var value) && value is not null && TryToDecimal(value, out var amount) && amount != 0)
            {
                return amount;
            }
        }

        return null;
    }

    /// <summary>
    /// Keeps only the columns that actually changed. Only meaningful for a
    /// single-row update with a captured "before" — bulk updates and updates
    /// where the pre-image couldn't be captured fall back to the untrimmed
    /// values, since there's no single "before" row to diff against.
    /// </summary>
    internal static (List<Dictionary<string, object?>>? Before, Dictionary<string, object?> After) DiffChangedFields(
        List<Dictionary<string, object?>>? before, Dictionary<string, object?> after)
    {
        if (before is not { Count: 1 })
        {
            return (before, after);
        }

        var beforeRow = before[0];
        var changedBefore = new Dictionary<string, object?>();
        var changedAfter = new Dictionary<string, object?>();

        foreach (var (key, newValue) in after)
        {
            if (key == "org_id")
            {
                continue;
            }

            var hadOld = beforeRow.TryGetValue(key, out var oldValue);
            if (ValuesEqual(oldValue, newValue))
            {
                continue;
            }

            if (hadOld)
            {
                changedBefore[key] = oldValue;
            }

            changedAfter[key] = newValue;
        }

        return (changedBefore.Count > 0 ? [changedBefore] : null, changedAfter);
    }

    /// <summary>
    /// String comparison rather than typed equality — the "before" side comes
    /// back from SqlDataReader typed as decimal/DateTime/etc, the "after" side
    /// is whatever the request's SqlParameter carried, and those two rarely
    /// share a CLR type even when the value is identical.
    /// </summary>
    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        // decimal's own equality is scale-sensitive (500m != 500.00m), and the
        // "before" side (typed straight off SqlDataReader) rarely shares the
        // exact same scale as the "after" side (whatever the request's
        // SqlParameter carried) even when the value is identical.
        if (TryToDecimal(a, out var da) && TryToDecimal(b, out var db))
        {
            return da == db;
        }

        return string.Equals(
            Convert.ToString(a, System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToString(b, System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    private static bool TryToDecimal(object value, out decimal result)
    {
        switch (value)
        {
            case decimal d: result = d; return true;
            case int i: result = i; return true;
            case long l: result = l; return true;
            case short s: result = s; return true;
            case double db: result = (decimal)db; return true;
            case float f: result = (decimal)f; return true;
            default: result = 0m; return false;
        }
    }
}
