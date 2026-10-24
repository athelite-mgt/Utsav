using Utsav.Domain.Finance;

namespace Utsav.UnitTests.Finance;

public class YearNamingTests
{
    [Theory]
    [InlineData("Durga Puja 2026", 2027, "Durga Puja 2027")]
    [InlineData("Kali Puja 2026", 2027, "Kali Puja 2027")]
    [InlineData("2026 Saraswati Puja", 2027, "2027 Saraswati Puja")]
    [InlineData("Barrackpore Kali Puja 2026 (Centenary)", 2027, "Barrackpore Kali Puja 2027 (Centenary)")]
    public void The_committees_own_wording_is_carried_forward_with_the_new_year(string previous, int year, string expected)
    {
        Assert.Equal(expected, YearNaming.Next(previous, year));
    }

    [Theory]
    [InlineData("Kali Puja", 2027, "Kali Puja 2027")]
    [InlineData("  Kali Puja  ", 2027, "Kali Puja 2027")]
    public void A_name_with_no_year_in_it_gets_one_appended(string previous, int year, string expected)
    {
        Assert.Equal(expected, YearNaming.Next(previous, year));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void With_no_previous_name_it_is_just_the_year_never_a_guessed_festival(string? previous)
    {
        Assert.Equal("2027", YearNaming.Next(previous, 2027));
    }

    [Fact]
    public void Only_the_last_year_in_a_name_is_replaced()
    {
        Assert.Equal("Cup 2025 Final 2027", YearNaming.Next("Cup 2025 Final 2026", 2027));
    }

    [Fact]
    public void Digits_that_are_not_a_year_are_left_alone()
    {
        Assert.Equal("Club 47 Puja 2027", YearNaming.Next("Club 47 Puja 2026", 2027));
        Assert.Equal("Block 12345 Puja 2027", YearNaming.Next("Block 12345 Puja", 2027));
    }
}
