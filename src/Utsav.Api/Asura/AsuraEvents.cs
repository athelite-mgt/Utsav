namespace Utsav.Api.Asura;

/// <summary>
/// What the browser receives over SSE while a question is being answered.
/// Each variant maps to one named SSE event the React client switches on.
/// </summary>
internal abstract record AsuraEvent(string Type);

/// <summary>
/// One line in the live ticker. Emitted from the orchestrator's real control
/// flow rather than from a canned script, which is the whole point: Boss asked
/// that complex work keep saying what it is doing <i>right now</i>, so a slow
/// query genuinely reads "Executing query…" for as long as the query is
/// actually running.
/// </summary>
internal sealed record StepEvent(string Text, bool Done) : AsuraEvent("step");

/// <summary>A chunk of the written answer, streamed so the caret moves while it arrives.</summary>
internal sealed record TokenEvent(string Text) : AsuraEvent("token");

internal sealed record TileDto(string Key, string Value, string? Detail, string? Tone);

internal sealed record ChartDto(
    string Kind,
    string LabelColumn,
    IReadOnlyList<string> ValueColumns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

/// <summary>
/// What a click on a chart bar or table row will ask for next. The browser
/// sends back only <see cref="ToolId"/> and a parameter value — never SQL — so
/// a drillable chart cannot become an arbitrary-query hole in the safety model.
/// </summary>
internal sealed record DrillDto(string ToolId, string ParamName, string FromColumn, string Label);

internal sealed record ProvenanceDto(
    string ToolId,
    string Title,
    IReadOnlyList<string> Tables,
    int RowCount,
    bool Truncated,
    long QueryMs,
    string Sql);

internal sealed record ResultEvent(
    string Title,
    IReadOnlyList<TileDto> Tiles,
    ChartDto? Chart,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    DrillDto? Drill,
    ProvenanceDto Provenance,
    // True when no model was involved: the report was matched by keyword and
    // the figures are the database's, but there is no written summary. Said
    // plainly in the UI, because a silently degraded answer is a lie of
    // omission.
    bool MatchedWithoutModel = false) : AsuraEvent("result");

/// <summary>
/// <paramref name="Detail"/> carries the per-tier failure reasons — "groq/...:
/// HTTP 403", and so on. Shown behind a disclosure in the UI rather than in the
/// headline, because the person asking wants to know what to do and whoever is
/// fixing it wants to know what broke. Asura is treasurer-level only and the
/// text names statuses and models, never keys.
/// </summary>
internal sealed record ErrorEvent(string Message, bool Recoverable, string? Detail = null)
    : AsuraEvent("error");

internal sealed record DoneEvent(string Model, string Provider, int Tier, long TotalMs)
    : AsuraEvent("done");
