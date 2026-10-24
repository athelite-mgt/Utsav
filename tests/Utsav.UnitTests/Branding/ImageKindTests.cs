using System.Text;
using Utsav.Api.Endpoints;

namespace Utsav.UnitTests.Branding;

public class ImageKindTests
{
    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];
    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46, 0, 1];
    private static byte[] Gif() => Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00\x00\x00\x00");
    private static byte[] Webp() => Encoding.ASCII.GetBytes("RIFF\x24\x00\x00\x00WEBPVP8 ");

    [Fact]
    public void Real_images_are_recognised_by_their_bytes()
    {
        Assert.Equal("png", ImageKind.Detect(Png())?.Extension);
        Assert.Equal("jpg", ImageKind.Detect(Jpeg())?.Extension);
        Assert.Equal("gif", ImageKind.Detect(Gif())?.Extension);
        Assert.Equal("webp", ImageKind.Detect(Webp())?.Extension);
        Assert.Equal("image/png", ImageKind.Detect(Png())?.ContentType);
    }

    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("<!DOCTYPE html><html><script>alert(1)</script></html>")]
    [InlineData("GIF")]
    [InlineData("plain text pretending to be a picture")]
    public void Text_markup_and_truncated_data_are_not_images(string content)
    {
        // SVG in particular is refused: it is markup, and served from our own
        // origin it could run script in a committee member's session.
        Assert.Null(ImageKind.Detect(Encoding.UTF8.GetBytes(content)));
    }

    [Fact]
    public void Empty_or_tiny_input_is_not_an_image()
    {
        Assert.Null(ImageKind.Detect([]));
        Assert.Null(ImageKind.Detect([0x89]));
    }

    [Fact]
    public void A_riff_container_that_is_not_webp_is_refused()
    {
        Assert.Null(ImageKind.Detect(Encoding.ASCII.GetBytes("RIFF\x24\x00\x00\x00WAVEfmt ")));
    }
}
