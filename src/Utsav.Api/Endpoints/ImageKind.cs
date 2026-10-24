namespace Utsav.Api.Endpoints;

/// <summary>
/// Decides what an uploaded file really is from its bytes. A client-supplied
/// content type or file name is a claim, not evidence, and a committee logo is
/// served back from our own origin, so only genuine raster images get through.
///
/// SVG is refused on purpose: it is markup that can carry script, and served
/// from this origin it would run in a committee member's session.
/// </summary>
public sealed record ImageKind(string Extension, string ContentType)
{
    private static readonly ImageKind Png = new("png", "image/png");
    private static readonly ImageKind Jpeg = new("jpg", "image/jpeg");
    private static readonly ImageKind Gif = new("gif", "image/gif");
    private static readonly ImageKind Webp = new("webp", "image/webp");

    public static ImageKind? Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return Png;
        }

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return Jpeg;
        }

        if (bytes.Length >= 6 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return Gif;
        }

        // RIFF container, and specifically a WEBP one: WAV and AVI share the header.
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return Webp;
        }

        return null;
    }

    public static ImageKind? ForExtension(string? extension) => extension?.ToLowerInvariant() switch
    {
        "png" => Png,
        "jpg" or "jpeg" => Jpeg,
        "gif" => Gif,
        "webp" => Webp,
        _ => null,
    };
}
