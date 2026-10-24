using Utsav.Domain.Tenancy;

namespace Utsav.UnitTests.Provisioning;

public class SlugTests
{
    [Theory]
    [InlineData("Barrackpore Kali Puja", "barrackpore-kali-puja")]
    [InlineData("  Kali -- Puja!!  ", "kali-puja")]
    [InlineData("Durga   Puja", "durga-puja")]
    [InlineData("Club 47's Puja", "club-47-s-puja")]
    [InlineData("---x---", "x")]
    public void A_name_becomes_a_short_url_safe_slug(string name, string expected)
    {
        Assert.Equal(expected, Slug.From(name));
    }

    [Fact]
    public void Bengali_names_keep_their_letters_rather_than_collapsing_to_nothing()
    {
        Assert.Equal("বারাকপুর-কালী-পূজা", Slug.From("বারাকপুর কালী পূজা"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void A_name_with_nothing_slug_worthy_in_it_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => Slug.From(name));
    }

    [Fact]
    public void Slugging_is_idempotent()
    {
        var once = Slug.From("Barrackpore Kali Puja");

        Assert.Equal(once, Slug.From(once));
    }
}
