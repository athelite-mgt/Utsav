using System.IO.Compression;
using System.Text;

namespace Utsav.UnitTests.Reports;

/// <summary>
/// A genuinely decodable PNG, built from scratch: a header-only PNG signature
/// is enough to be DETECTED as an image but not to be decoded and drawn, and
/// the difference matters to anything that renders the logo into a PDF.
/// </summary>
internal static class TestImages
{
    public static byte[] Png(int width = 24, int height = 24, byte r = 217, byte g = 119, byte b = 6)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0); // filter: none
                for (var x = 0; x < width; x++)
                {
                    z.WriteByte(r);
                    z.WriteByte(g);
                    z.WriteByte(b);
                }
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(png, "IHDR", [.. BigEndian(width), .. BigEndian(height), 8, 2, 0, 0, 0]);
        Chunk(png, "IDAT", raw.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static byte[] BigEndian(int v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var t = Encoding.ASCII.GetBytes(type);
        s.Write(BigEndian(data.Length));
        s.Write(t);
        s.Write(data);
        s.Write(BigEndian((int)Crc32([.. t, .. data])));
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
