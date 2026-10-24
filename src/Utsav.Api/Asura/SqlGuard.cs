using System.Text;
using System.Text.RegularExpressions;

namespace Utsav.Api.Asura;

/// <summary>
/// Static validation of a SELECT statement before it is allowed anywhere near
/// the committee's database. Layer 2 of Asura's four safety layers (layer 1 is
/// <see cref="AsuraCatalog"/>, layer 3 is the always-rolled-back transaction in
/// <see cref="ReadOnlyQueryExecutor"/>, layer 4 is that executor's row and time
/// caps).
///
/// <para>
/// Pure and I/O-free so it can be tested adversarially without a database —
/// which is the only way a check like this earns any trust. See
/// <c>SqlGuardTests</c>.
/// </para>
///
/// <para>
/// The work is done in two phases, and the order matters. <see cref="Normalize"/>
/// first walks the statement character by character, tracking whether it is
/// inside a string literal, a line comment, a block comment or a bracketed
/// identifier. Only then are the rules applied, to that normalised text.
/// Running regexes over the raw SQL instead — the obvious shortcut — is wrong in
/// both directions: <c>WHERE notes = '-- nothing'</c> would have its tail eaten
/// as a comment, and <c>WHERE name = ''; DROP TABLE donors --'</c> would hide a
/// second statement inside what a naive scan reads as a literal. Replacing every
/// literal with an empty one removes that entire class of trick before any rule
/// runs.
/// </para>
/// </summary>
internal static partial class SqlGuard
{
    /// <summary>Longest statement accepted, as a cheap backstop against pathological input.</summary>
    private const int MaxLength = 8_000;

    /// <summary>
    /// Tokens that have no business in a read-only query. <c>into</c> is here
    /// because <c>SELECT … INTO new_table</c> creates and populates a table —
    /// a write, reached without the word INSERT appearing anywhere.
    /// <c>waitfor</c> is here because <c>WAITFOR DELAY</c> turns a read into a
    /// denial of service against the committee's own app.
    /// </summary>
    private static readonly string[] ForbiddenKeywords =
    [
        "insert", "update", "delete", "drop", "alter", "create", "truncate",
        "merge", "exec", "execute", "grant", "revoke", "deny", "backup",
        "restore", "shutdown", "reconfigure", "waitfor", "into",
        "openrowset", "openquery", "opendatasource", "openxml", "bulk",
        "dbcc", "kill", "sp_", "xp_", "fn_", "sys.", "sysobjects", "syscolumns",
        "information_schema",
    ];

    public readonly record struct Result(bool Ok, string? Error, string? Normalized)
    {
        public static Result Fail(string error) => new(false, error, null);
        public static Result Pass(string normalized) => new(true, null, normalized);
    }

    public static Result Validate(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Result.Fail("Empty query.");
        }

        if (sql.Length > MaxLength)
        {
            return Result.Fail($"Query exceeds {MaxLength} characters.");
        }

        var (normalized, scanError) = Normalize(sql);
        if (scanError is not null)
        {
            return Result.Fail(scanError);
        }

        // One statement only. A trailing semicolon is fine; anything after it
        // is a second statement and is refused outright rather than truncated,
        // because silently running only the first half of something a caller
        // wrote is its own kind of surprise.
        var semicolon = normalized.IndexOf(';');
        if (semicolon >= 0 && normalized[(semicolon + 1)..].Trim().Length > 0)
        {
            return Result.Fail("Only a single statement is allowed.");
        }

        var statement = (semicolon >= 0 ? normalized[..semicolon] : normalized).Trim();
        if (statement.Length == 0)
        {
            return Result.Fail("Empty query.");
        }

        // A leading CTE is perfectly read-only, and the strongest model in the
        // benchmark used one to resolve the active puja year. An earlier
        // "must start with SELECT" check rejected exactly that good answer.
        if (!statement.StartsWith("select", StringComparison.Ordinal)
            && !statement.StartsWith("with", StringComparison.Ordinal))
        {
            return Result.Fail("Only SELECT (or WITH … SELECT) queries are allowed.");
        }

        foreach (var keyword in ForbiddenKeywords)
        {
            if (ContainsToken(statement, keyword))
            {
                return Result.Fail($"Disallowed keyword '{keyword}' in a read-only query.");
            }
        }

        // Every query must be scoped to the caller's organisation, and the only
        // way to be scoped is the bound parameter — never a literal the model
        // chose for itself.
        if (!statement.Contains("@orgid", StringComparison.Ordinal))
        {
            return Result.Fail("Query must be scoped with the @orgId parameter.");
        }

        if (ReferencedObjects(statement) is { } offending && offending.Count > 0)
        {
            return Result.Fail(
                $"Query references objects outside the catalogue: {string.Join(", ", offending)}.");
        }

        return Result.Pass(statement);
    }

    /// <summary>
    /// Lower-cases the statement, strips comments, empties string literals and
    /// unwraps bracketed identifiers (so <c>[users]</c> is still recognisable as
    /// <c>users</c> by the catalogue check, rather than slipping past it as
    /// punctuation).
    /// </summary>
    internal static (string Normalized, string? Error) Normalize(string sql)
    {
        var sb = new StringBuilder(sql.Length);

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];

            // ---- string literal ----------------------------------------
            if (c == '\'')
            {
                i++;
                var closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        // '' is an escaped quote inside the literal, not the end.
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }

                        closed = true;
                        break;
                    }

                    i++;
                }

                if (!closed)
                {
                    return (string.Empty, "Unterminated string literal.");
                }

                // The contents are deliberately discarded: a literal can never
                // change what the statement *does*, so nothing inside one
                // should be able to trip or evade a rule.
                sb.Append("''");
                continue;
            }

            // ---- line comment -------------------------------------------
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            // ---- block comment (SQL Server allows nesting) --------------
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                var depth = 1;
                i += 2;
                while (i < sql.Length && depth > 0)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                if (depth > 0)
                {
                    return (string.Empty, "Unterminated block comment.");
                }

                sb.Append(' ');
                i--; // the for-loop's i++ would otherwise skip a character
                continue;
            }

            // ---- bracketed identifier -----------------------------------
            if (c == '[')
            {
                i++;
                var closed = false;
                while (i < sql.Length)
                {
                    if (sql[i] == ']')
                    {
                        closed = true;
                        break;
                    }

                    sb.Append(char.ToLowerInvariant(sql[i]));
                    i++;
                }

                if (!closed)
                {
                    return (string.Empty, "Unterminated bracketed identifier.");
                }

                continue;
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        // Collapse whitespace so the rules see a predictable shape and a
        // keyword cannot be split across a newline.
        return (WhitespaceRun().Replace(sb.ToString(), " ").Trim(), null);
    }

    /// <summary>
    /// Whole-token containment. Prevents <c>updated_at</c> from matching
    /// <c>update</c> and <c>created_on</c> from matching <c>create</c>, which a
    /// plain substring test would flag — those columns are all over this
    /// schema, so a naive check would reject almost every real query.
    /// Prefix-style markers (<c>sp_</c>, <c>sys.</c>) are matched as substrings
    /// on purpose, since that is how they are actually written.
    /// </summary>
    private static bool ContainsToken(string haystack, string token)
    {
        if (token.EndsWith('_') || token.EndsWith('.'))
        {
            return haystack.Contains(token, StringComparison.Ordinal);
        }

        var from = 0;
        while (true)
        {
            var at = haystack.IndexOf(token, from, StringComparison.Ordinal);
            if (at < 0)
            {
                return false;
            }

            var beforeOk = at == 0 || !IsIdentifierChar(haystack[at - 1]);
            var afterIndex = at + token.Length;
            var afterOk = afterIndex >= haystack.Length || !IsIdentifierChar(haystack[afterIndex]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            from = at + 1;
        }
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Names appearing after FROM or JOIN that are neither in the catalogue nor
    /// a CTE the query defined itself. Returns the offenders, so the error can
    /// say which name was the problem.
    /// </summary>
    private static List<string> ReferencedObjects(string statement)
    {
        var cteNames = CteName().Matches(statement)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = new List<string>();
        foreach (var match in SourceObject().Matches(statement).Cast<Match>())
        {
            var name = match.Groups[2].Value;

            // Strip a dbo. qualifier; anything else qualified is already
            // refused by the "sys." / three-part-name keyword rules.
            var dot = name.LastIndexOf('.');
            if (dot >= 0)
            {
                name = name[(dot + 1)..];
            }

            if (cteNames.Contains(name) || AsuraCatalog.IsReadable(name))
            {
                continue;
            }

            if (!offenders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                offenders.Add(name);
            }
        }

        return offenders;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    /// <summary>Matches <c>with x as (</c> and the <c>, y as (</c> that follow it.</summary>
    [GeneratedRegex(@"(?:\bwith\s+|,\s*)([a-z_][a-z0-9_]*)\s+as\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex CteName();

    /// <summary>
    /// Matches the table name in <c>from x</c> / <c>join x</c>. A following
    /// <c>(</c> (a subquery or a function call) does not match the identifier
    /// pattern, so derived tables are simply skipped rather than misread as a
    /// table called "(".
    /// </summary>
    [GeneratedRegex(@"\b(from|join)\s+([a-z_][a-z0-9_.]*)", RegexOptions.IgnoreCase)]
    private static partial Regex SourceObject();
}
