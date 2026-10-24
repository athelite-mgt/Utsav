using Utsav.Infrastructure.Data;

namespace Utsav.UnitTests.Provisioning;

public class FestivalTemplatesTests
{
    private static IEnumerable<string> Days(FestivalTemplate t) => t.PujaDays;

    [Fact]
    public void Kali_puja_does_not_inherit_the_durga_calendar()
    {
        var kali = FestivalTemplates.For("kali");

        // Seeding Mahalaya..Dasami into a Kali committee would be visibly wrong
        // on its Schedule page from the first day.
        Assert.Contains("Kali Puja", Days(kali));
        Assert.DoesNotContain("Mahalaya", Days(kali));
        Assert.DoesNotContain("Dasami", Days(kali));
    }

    [Fact]
    public void Durga_puja_keeps_its_eight_day_calendar_in_order()
    {
        Assert.Equal(
            ["Mahalaya", "Panchami", "Sasthi", "Saptami", "Astami", "Sandhi Puja", "Nabami", "Dasami"],
            FestivalTemplates.For("durga").PujaDays);
    }

    [Theory]
    [InlineData("KALI")]
    [InlineData("  Kali ")]
    public void Festival_names_are_matched_loosely(string festival)
    {
        Assert.Equal("Kali Puja", FestivalTemplates.For(festival).DisplayName);
    }

    [Fact]
    public void An_unknown_festival_still_gets_a_usable_generic_template()
    {
        // A committee with a festival we have not met must still be able to
        // start. The day list is editable in Settings afterwards.
        var generic = FestivalTemplates.For("jagadhatri");

        Assert.NotEmpty(generic.PujaDays);
        Assert.NotEmpty(generic.Lookups);
    }

    [Theory]
    [InlineData("durga")]
    [InlineData("kali")]
    [InlineData("saraswati")]
    [InlineData("lakshmi")]
    [InlineData("anything-else")]
    public void Every_template_carries_the_lookup_categories_the_app_depends_on(string festival)
    {
        // The UI offers these as dropdowns; a template missing one would give a
        // new committee an empty list on a page the old app always populated.
        var categories = FestivalTemplates.For(festival).Lookups.Select(l => l.Category).ToHashSet();

        foreach (var required in new[]
                 {
                     "payment_mode", "expense_category", "ad_type", "ad_status",
                     "offering_type", "donation_purpose", "vendor_category",
                 })
        {
            Assert.Contains(required, categories);
        }
    }

    [Fact]
    public void Festival_specific_expense_categories_do_not_leak_into_other_festivals()
    {
        string[] Expense(string f) => FestivalTemplates.For(f).Lookups.Single(l => l.Category == "expense_category").Labels;

        Assert.Contains("Khuti Puja", Expense("durga"));
        Assert.DoesNotContain("Khuti Puja", Expense("kali"));
        Assert.DoesNotContain("Lakshmi Puja", Expense("kali"));
    }

    [Fact]
    public void Known_festivals_are_listed_for_the_console()
    {
        Assert.Equal(["durga", "kali", "saraswati", "lakshmi"], FestivalTemplates.Known);
    }

    [Fact]
    public void No_lookup_label_is_duplicated_within_a_category()
    {
        // lookups has a unique index on (org, category, label): a duplicate
        // would make provisioning fail on its very first insert.
        foreach (var festival in FestivalTemplates.Known.Append("generic"))
        {
            var template = FestivalTemplates.For(festival);
            var all = template.Lookups.Append(("puja_day", template.PujaDays));
            foreach (var (category, labels) in all)
            {
                Assert.True(labels.Distinct().Count() == labels.Length, $"{festival}/{category} has a duplicate label");
            }
        }
    }
}
