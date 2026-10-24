using System.Text.Json;

namespace Utsav.Api.Endpoints;

internal static class SqlValue
{
    /// <summary>
    /// Ported from <c>asDbValue()</c> in <c>server/app.mjs</c>: turns a value
    /// off the JSON request body into something the driver can bind.
    ///
    /// The object/array case matters — <c>subscriptions.history</c> is sent as
    /// a JSON object and stored as text, so it is re-serialised rather than
    /// rejected, exactly as <c>JSON.stringify</c> did.
    ///
    /// Numbers stay <see cref="decimal"/> rather than becoming
    /// <see cref="double"/>: every numeric column here is money, and binding a
    /// binary float would reintroduce the rounding drift the schema's
    /// NUMERIC(12,2) columns exist to prevent.
    /// </summary>
    public static object FromJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => DBNull.Value,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.GetString() is { } s ? s : DBNull.Value,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l : value.GetDecimal(),
        JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
        _ => DBNull.Value,
    };
}
