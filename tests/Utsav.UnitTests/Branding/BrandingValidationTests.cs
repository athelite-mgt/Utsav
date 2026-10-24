using Utsav.Api.Endpoints;

namespace Utsav.UnitTests.Branding;

public class BrandingValidationTests
{
    [Theory]
    [InlineData("#ff0000")]
    [InlineData("#FFF")]
    [InlineData("#1a2b3c")]
    [InlineData("#B45309")]
    public void Plain_hex_colours_are_accepted(string value)
    {
        Assert.True(BrandingValidation.IsValidAccentColour(value));
    }

    [Theory]
    [InlineData("red")]
    [InlineData("rgb(1,2,3)")]
    [InlineData("hsl(10 50% 50%)")]
    [InlineData("#ff00")]
    [InlineData("#12345g")]
    [InlineData("ff0000")]
    [InlineData("")]
    [InlineData(" #fff")]
    [InlineData("#fff ")]
    // The ones that matter: the value is written into a CSS custom property, so
    // anything that can close the declaration and open another is stored CSS
    // injection. An allowlist leaves nothing to bypass.
    [InlineData("#fff;} body{display:none")]
    [InlineData("#fff\n}")]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("#fff/**/")]
    public void Anything_that_is_not_a_plain_hex_colour_is_rejected(string value)
    {
        Assert.False(BrandingValidation.IsValidAccentColour(value));
    }

    [Fact]
    public void Null_means_no_brand_colour_and_is_allowed()
    {
        Assert.True(BrandingValidation.IsValidAccentColour(null));
    }

    [Fact]
    public void A_stored_colour_is_normalised_to_lower_case()
    {
        Assert.Equal("#b45309", BrandingValidation.NormaliseAccentColour("#B45309"));
        Assert.Null(BrandingValidation.NormaliseAccentColour(null));
    }
}
