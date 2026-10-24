using System.Text.Json;
using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// Whether a request body actually binds the way the frontend sends it.
///
/// <para>
/// This is the bug behind "PDF and Excel download from Asura is not working".
/// <c>Program.cs</c> configures <c>PropertyNamingPolicy = SnakeCaseLower</c>
/// for the whole app's JSON — both serialization <i>and</i> request-body
/// binding — because the app's convention is snake_case over the wire. But
/// <c>asuraStream.ts</c> was posting <c>{"toolId": "..."}</c>, and ASP.NET's
/// binder matches an incoming JSON key against each property's policy-
/// converted name <b>literally</b>, not case-insensitively across naming
/// conventions: "toolId" is simply a different string from the expected
/// "tool_id", so <c>DrillRequest.ToolId</c> and <c>ExportRequest.ToolId</c>
/// silently bound to null on every call, and both endpoints 400'd with
/// "Unknown report."
/// </para>
///
/// <para>
/// It hid behind <c>askAsura</c> working fine, because <c>AskRequest</c>'s
/// only field is <c>Question</c> — and "question" is identical whether you
/// convert it from camelCase or from PascalCase-to-snake_case. One field
/// across the whole feature happened to be naming-convention-invariant, and
/// it was the one every manual test exercised.
/// </para>
///
/// <para>
/// No WebApplicationFactory here: this targets the exact mechanism — the
/// configured <see cref="JsonSerializerOptions"/> deserializing the literal
/// bytes a browser sends — without standing up a server, a database or auth
/// to prove it.
/// </para>
/// </summary>
public class AsuraRequestBindingTests
{
    /// <summary>The exact policy Program.cs configures for the whole app.</summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void Drill_request_binds_from_the_snake_case_body_the_frontend_now_sends()
    {
        var json = """{"tool_id":"zone_donor_detail","params":{"zoneId":"abc-123"}}""";

        var request = JsonSerializer.Deserialize<DrillRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal("zone_donor_detail", request.ToolId);
        Assert.Equal("abc-123", request.Params?["zoneId"]);
    }

    [Fact]
    public void Export_request_binds_from_the_snake_case_body_the_frontend_now_sends()
    {
        var json = """{"tool_id":"budget_vs_actual","format":"pdf","params":{},"question":"How are we doing?"}""";

        var request = JsonSerializer.Deserialize<ExportRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Equal("budget_vs_actual", request.ToolId);
        Assert.Equal("pdf", request.Format);
        Assert.Equal("How are we doing?", request.Question);
    }

    /// <summary>
    /// The regression itself, pinned so it cannot silently come back. This is
    /// exactly what the old <c>asuraStream.ts</c> sent, and exactly why both
    /// endpoints answered "Unknown report." instead of opening a drill-down or
    /// producing a file.
    /// </summary>
    [Fact]
    public void A_camelCase_tool_id_does_not_bind_under_the_apps_naming_policy()
    {
        var json = """{"toolId":"zone_donor_detail","params":{}}""";

        var request = JsonSerializer.Deserialize<DrillRequest>(json, Options);

        Assert.NotNull(request);
        Assert.Null(request.ToolId);
    }

    /// <summary>
    /// Documents why this one field never exposed the bug: a single word is
    /// identical whether converted from camelCase or from PascalCase-to-
    /// snake_case, so askAsura kept working throughout.
    /// </summary>
    [Fact]
    public void A_single_word_field_binds_identically_either_way()
    {
        var request = JsonSerializer.Deserialize<AskRequest>("""{"question":"hi"}""", Options);
        Assert.Equal("hi", request?.Question);
    }

    /// <summary>
    /// What the dictionary VALUES inside <c>params</c> look like is not
    /// subject to the naming policy — only top-level record property names
    /// are — so a parameter like <c>zoneId</c> survives verbatim and still
    /// matches <c>AsuraTool</c>'s own camelCase parameter names
    /// (<see cref="ToolCatalogue"/>). Only the request's own top-level
    /// property names needed fixing.
    /// </summary>
    [Fact]
    public void Dictionary_keys_inside_params_are_not_touched_by_the_naming_policy()
    {
        var json = """{"tool_id":"zone_donor_detail","params":{"zoneId":"x","yearId":"y"}}""";
        var request = JsonSerializer.Deserialize<DrillRequest>(json, Options);

        Assert.Equal("x", request?.Params?["zoneId"]);
        Assert.Equal("y", request?.Params?["yearId"]);
    }
}
