using System.Security.Claims;
using System.Text.Json;
using Utsav.Api.Authentication;
using Utsav.Domain.Authorization;
using Utsav.Infrastructure.Authentication;

namespace Utsav.Api.Asura;

internal sealed record AskRequest(string Question);

internal sealed record DrillRequest(string ToolId, Dictionary<string, string>? Params);

internal sealed record ExportRequest(
    string ToolId, string? Format, Dictionary<string, string>? Params, string? Question);

internal static class AsuraEndpoints
{
    /// <summary>
    /// Matches the app-wide snake_case policy set in Program.cs, so the SSE
    /// payloads look like every other response the frontend consumes.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Maps Asura's routes, or none of them when the feature is off.
    ///
    /// <para>
    /// Returning early means the routes do not exist rather than existing and
    /// returning 403 — a disabled Asura is indistinguishable from an Asura that
    /// was never deployed, which is exactly what makes this safe to ship to
    /// production ahead of being switched on.
    /// </para>
    /// </summary>
    public static WebApplication MapAsuraEndpoints(this WebApplication app)
    {
        var options = app.Services.GetService<AsuraOptions>();
        if (options is null || !options.Enabled)
        {
            return app;
        }

        var group = app.MapGroup("/api/asura");

        // The report menu, for the UI's suggestion chips and its "I can't
        // answer that" fallback list. Titles and questions only — never SQL.
        group.MapGet("/reports", () => Results.Ok(
                ToolCatalogue.Tools.Values.Select(t => new
                {
                    id = t.Id,
                    title = t.Title,
                    question = t.Question,
                    chart = t.Chart,
                    drills = t.Drill is not null,
                })))
            .RequirePermission(Permission.UseAsura);

        // POST rather than GET even though SSE is conventionally GET, so the
        // question travels in the body. A question can name a donor, and a
        // query string ends up in access logs and browser history.
        // EventSource is GET-only, so the client reads this with fetch +
        // ReadableStream instead.
        group.MapPost("/ask", AskAsync).RequirePermission(Permission.UseAsura);

        // A drill-down runs a catalogued report directly, with no model in the
        // loop: the browser sends a tool id and parameters, never SQL.
        group.MapPost("/drill", DrillAsync).RequirePermission(Permission.UseAsura);

        group.MapPost("/export", ExportAsync).RequirePermission(Permission.UseAsura);

        // Why isn't it working? — answered without reading App Service logs.
        //
        // Reports what Asura actually resolved per tier: which provider, which
        // model, whether a key arrived, and whether the tier can issue a
        // request. Keys are reported as a boolean, a length and a format
        // check, never a value: a length is enough to tell "absent" from
        // "present but truncated by a copy-paste", and a prefix match (a
        // public convention — gsk_ for every Groq customer, nvapi- for every
        // NVIDIA customer, not secret to this one) is enough to tell "this
        // looks like the right kind of key" from "this is the wrong value
        // entirely" without ever reading the key itself.
        //
        // The format check exists because of a real incident: Asura:Enabled
        // and both keys were set, usable_tiers reported 3, and every real
        // question still failed — but with a clean HTTP 401 from both
        // providers, not a timeout or connection error. A 401 is proof the
        // request reached the provider and was rejected for its contents, not
        // that egress was blocked, which was the original (wrong) suspicion.
        // api_key_looks_valid turns "something is subtly wrong with the pasted
        // value" into a single boolean, checkable from the same screen that
        // already shows usable_tiers true.
        group.MapGet("/diagnostics", (AsuraOptions opts) => Results.Ok(new
            {
                enabled = opts.Enabled,
                allow_freeform_sql = opts.AllowFreeformSql,
                groq_api_key_present = !string.IsNullOrWhiteSpace(opts.GroqApiKey),
                nvidia_api_key_present = !string.IsNullOrWhiteSpace(opts.NvidiaApiKey),
                usable_tiers = opts.Providers.Count(AsuraOptions.IsUsable),
                tiers = opts.Providers.Select((p, i) => new
                {
                    tier = i + 1,
                    provider = p.Provider,
                    model = p.Model,
                    base_url = p.BaseUrl,
                    api_key_present = !string.IsNullOrWhiteSpace(p.ApiKey),
                    api_key_length = p.ApiKey?.Length ?? 0,
                    api_key_looks_valid = AsuraOptions.LooksLikeValidKey(p),
                    expected_key_prefix = AsuraOptions.ExpectedKeyPrefix(p.Provider),
                    usable = AsuraOptions.IsUsable(p),
                }),
                reports = ToolCatalogue.Tools.Count,
            }))
            .RequirePermission(Permission.UseAsura);

        return app;
    }

    /// <summary>
    /// Re-runs the report server-side and renders it, rather than formatting
    /// rows posted by the browser. A download is what gets printed, filed and
    /// argued over at a committee meeting, so it must match the database — not
    /// what a stale tab still shows, and not something a crafted request could
    /// substitute.
    /// </summary>
    private static async Task<IResult> ExportAsync(
        ExportRequest body,
        ReadOnlyQueryExecutor executor,
        CancellationToken ct)
    {
        if (!ToolCatalogue.TryGet(body.ToolId, out var tool))
        {
            return Results.BadRequest(new { error = "Unknown report." });
        }

        var format = (body.Format ?? "pdf").ToLowerInvariant();
        if (format is not ("pdf" or "xlsx"))
        {
            return Results.BadRequest(new { error = "Format must be pdf or xlsx." });
        }

        var bound = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in tool.Parameters)
        {
            string? raw = null;
            body.Params?.TryGetValue(p.Name, out raw);

            object? value = string.IsNullOrWhiteSpace(raw)
                ? null
                : p.Type switch
                {
                    "guid" => Guid.TryParse(raw, out var g) ? g : null,
                    "int" => int.TryParse(raw, out var i) ? i : null,
                    _ => raw,
                };

            if (value is null && p.Required)
            {
                return Results.BadRequest(new { error = $"This report needs {p.Name}." });
            }

            bound[p.Name] = value;
        }

        try
        {
            var result = await executor.ExecuteAsync(tool.Sql, SessionService.DefaultOrgId, bound, ct);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");

            return format == "pdf"
                ? Results.File(AsuraExport.Pdf(tool, result, body.Question),
                    "application/pdf", $"{tool.Id}-{stamp}.pdf")
                : Results.File(AsuraExport.Excel(tool, result, body.Question),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"{tool.Id}-{stamp}.xlsx");
        }
        catch (AsuraRefusedException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task AskAsync(
        HttpContext http,
        AskRequest body,
        AsuraOrchestrator orchestrator,
        AsuraRateLimiter limiter,
        CancellationToken ct)
    {
        var response = http.Response;

        if (string.IsNullOrWhiteSpace(body.Question))
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsJsonAsync(new { error = "Ask a question." }, ct);
            return;
        }

        var userId = CallerId(http.User);
        if (userId is null)
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!limiter.TryRecord(userId.Value, out var retryAfter))
        {
            response.StatusCode = StatusCodes.Status429TooManyRequests;
            response.Headers.RetryAfter = retryAfter.ToString();
            await response.WriteAsJsonAsync(
                new { error = "That's a lot of questions in an hour — try again shortly." }, ct);
            return;
        }

        response.Headers.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        // Azure App Service sits behind a reverse proxy that will otherwise
        // buffer the whole response and deliver it at once, which would defeat
        // the entire point of streaming the steps as they happen.
        response.Headers["X-Accel-Buffering"] = "no";
        await response.Body.FlushAsync(ct);

        await orchestrator.AskAsync(
            body.Question.Trim(),
            SessionService.DefaultOrgId,
            (e, token) => WriteEventAsync(response, e, token),
            ct);
    }

    private static async Task<IResult> DrillAsync(
        HttpContext http,
        DrillRequest body,
        ReadOnlyQueryExecutor executor,
        CancellationToken ct)
    {
        if (!ToolCatalogue.TryGet(body.ToolId, out var tool))
        {
            // An unknown id is refused outright rather than guessed at — this
            // is the one endpoint a client names a query on, so it has to be
            // the narrowest possible door.
            return Results.BadRequest(new { error = "Unknown report." });
        }

        var bound = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in tool.Parameters)
        {
            string? raw = null;
            body.Params?.TryGetValue(p.Name, out raw);

            object? value = string.IsNullOrWhiteSpace(raw)
                ? null
                : p.Type switch
                {
                    "guid" => Guid.TryParse(raw, out var g) ? g : null,
                    "int" => int.TryParse(raw, out var i) ? i : null,
                    _ => raw,
                };

            if (value is null && p.Required)
            {
                return Results.BadRequest(new { error = $"This report needs {p.Name}." });
            }

            bound[p.Name] = value;
        }

        try
        {
            var result = await executor.ExecuteAsync(tool.Sql, SessionService.DefaultOrgId, bound, ct);
            return Results.Ok(new
            {
                title = tool.Title,
                columns = result.Columns,
                rows = result.Rows,
                truncated = result.Truncated,
                query_ms = result.ElapsedMs,
                chart = tool.Chart,
                label_column = tool.LabelColumn,
                value_columns = tool.ValueColumns,
                tables = AsuraOrchestrator.TablesIn(tool.Sql),
                drill = tool.Drill is null
                    ? null
                    : new { tool_id = tool.Drill.ToolId, param_name = tool.Drill.ParamName, from_column = tool.Drill.FromColumn },
            });
        }
        catch (AsuraRefusedException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// One SSE frame. Data is written as a single line of JSON so an embedded
    /// newline — easily present in a donor's note or a model's prose — cannot
    /// terminate the frame early and corrupt the stream.
    /// </summary>
    private static async Task WriteEventAsync(HttpResponse response, AsuraEvent e, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(e, e.GetType(), Json);
        await response.WriteAsync($"event: {e.Type}\ndata: {payload}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static Guid? CallerId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
