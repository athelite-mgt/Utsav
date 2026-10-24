using Utsav.Api.Asura;

namespace Utsav.UnitTests.Asura;

/// <summary>
/// The model-less fallback's report picker. It runs when every provider is
/// unreachable, so its job is to be right or to abstain — a wrong report
/// presented as the answer is worse than no answer, because the figures look
/// authoritative either way.
/// </summary>
public class KeywordToolMatcherTests
{
    [Theory]
    [InlineData("Which zones are behind on collections this year?", "collections_by_zone")]
    [InlineData("show me the worst performing areas", "collections_by_zone")]
    [InlineData("how is each collector doing?", "collections_by_collector")]
    [InlineData("who hasn't paid yet", "donor_dues")]
    [InlineData("list the outstanding dues", "donor_dues")]
    [InlineData("are we over budget anywhere?", "budget_vs_actual")]
    [InlineData("what did we spend by category", "expenses_by_category")]
    [InlineData("which vendors were paid the most", "vendor_spend")]
    [InlineData("any unsettled advances?", "advances_outstanding")]
    [InlineData("show the monthly trend", "monthly_collection_trend")]
    public void Matches_the_report_a_treasurer_meant(string question, string expected)
    {
        Assert.Equal(expected, KeywordToolMatcher.Match(question));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Abstains_on_empty_input(string? question)
    {
        Assert.Null(KeywordToolMatcher.Match(question!));
    }

    [Theory]
    [InlineData("what is the weather in Kolkata tomorrow")]
    [InlineData("write me a poem about the dhak")]
    [InlineData("hello")]
    public void Abstains_when_the_question_is_not_about_the_books(string question)
    {
        // Without a model there is nothing to fall back on but keywords, so an
        // off-topic question must yield nothing rather than the nearest report.
        Assert.Null(KeywordToolMatcher.Match(question));
    }

    [Fact]
    public void Abstains_rather_than_guessing_between_two_equally_likely_reports()
    {
        // Reads equally like vendor spend and like spend-by-category. Picking
        // one would present the wrong totals as the answer to what was asked.
        Assert.Null(KeywordToolMatcher.Match("vendor category"));
    }

    [Fact]
    public void Never_picks_a_report_that_needs_a_parameter_it_cannot_supply()
    {
        // The detail reports need a zone or collector id, which a sentence
        // cannot provide — they are only ever reached by clicking a row. Asked
        // for directly they must abstain, not run with a null parameter and
        // return nothing while looking like a real answer.
        foreach (var question in new[] { "donors in a zone", "bills in a category", "donors for collector" })
        {
            var match = KeywordToolMatcher.Match(question);
            if (match is not null)
            {
                Assert.True(ToolCatalogue.TryGet(match, out var tool));
                Assert.DoesNotContain(tool.Parameters, p => p.Required);
            }
        }
    }

    [Fact]
    public void Longer_phrases_outweigh_single_words()
    {
        // "not paid" is strong evidence of the dues report; "paid" alone also
        // appears in vendor spend. The phrase has to win.
        Assert.Equal("donor_dues", KeywordToolMatcher.Match("who has not paid"));
    }

    [Fact]
    public void Punctuation_and_casing_do_not_defeat_a_match()
    {
        Assert.Equal("collections_by_zone", KeywordToolMatcher.Match("ZONES?!"));
    }

    [Fact]
    public void Every_report_it_can_return_actually_exists()
    {
        // A trigger list pointing at a renamed report would silently stop
        // matching; this fails the build instead.
        foreach (var question in new[]
                 {
                     "zones", "collector", "dues", "budget", "expenses", "vendors",
                     "advances", "income mix", "monthly trend", "overall summary",
                 })
        {
            var match = KeywordToolMatcher.Match(question);
            if (match is not null)
            {
                Assert.True(ToolCatalogue.TryGet(match, out _), $"'{question}' matched unknown report '{match}'");
            }
        }
    }
}
