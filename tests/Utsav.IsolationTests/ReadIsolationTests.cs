using System.Net;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// Every GET that returns committee data, driven through the real app. A leak
/// is detected by content, not by a schema assumption: the other committee's
/// id or marker must appear nowhere in the response body, whatever shape it
/// takes. A new data endpoint added without a line here is exactly the gap
/// this suite exists to close, so adding an endpoint means adding a route.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class ReadIsolationTests(DatabaseFixture fixture)
{
    /// <summary>
    /// (route template, whether the seed gives it own-committee data to find).
    /// {year} is replaced with the caller's own puja year id.
    /// </summary>
    public static TheoryData<string, bool> Routes => new()
    {
        { "/api/zones", true },
        { "/api/collectors", true },
        { "/api/lookups", true },
        { "/api/puja_years", true },
        { "/api/subscriptions?year_id={year}", true },
        { "/api/feasts?year_id={year}", false },
        { "/api/ad_bookings?year_id={year}", false },
        { "/api/special_donations?year_id={year}", false },
        { "/api/offerings?year_id={year}", false },
        { "/api/expenses?year_id={year}", true },
        { "/api/feast_expenses?year_id={year}", false },
        { "/api/budget?year_id={year}", true },
        { "/api/cash_ledger?year_id={year}", true },
        { "/api/fund_transfers?year_id={year}", false },
        { "/api/vendors", true },
        { "/api/vendor_issues", false },
        { "/api/contracts?year_id={year}", true },
        { "/api/advances?year_id={year}", true },
        { "/api/puja_schedule?year_id={year}", false },
        { "/api/puja_calendar_days?year_id={year}", false },
        { "/api/workflow_items?year_id={year}", true },
        { "/api/documents", true },
        { "/api/api_keys", true },
        { "/api/users", false },
        { "/api/access_requests", false },
        { "/api/notifications", false },
        { "/api/feast_tokens?year_id={year}", false },
        { "/api/feast_tokens/summary?year_id={year}", false },
        { "/api/collection_payments?parent_type=subscriptions&parent_id={sub}", false },
        { "/api/collection_payments/by_stream?year_id={year}", false },
        { "/api/collection_payments/by_zone?year_id={year}", false },
        { "/api/collection_payments/by_mode?year_id={year}", false },
        { "/api/collection_payments/by_date?year_id={year}", false },
        { "/api/collection_payments/for_year?year_id={year}", false },
        { "/api/fund_position?year_id={year}", false },
        { "/api/fund_position/breakdown?year_id={year}", false },
        { "/api/my/advances?year_id={year}", false },
    };

    private static string Url(string template, TenantData own) =>
        template.Replace("{year}", own.Year.ToString()).Replace("{sub}", own.Subscription.ToString());

    private static bool Mentions(string body, TenantData tenant) =>
        body.Contains(tenant.Marker, StringComparison.Ordinal)
        || body.Contains(tenant.Org.ToString(), StringComparison.OrdinalIgnoreCase)
        || body.Contains(tenant.Year.ToString(), StringComparison.OrdinalIgnoreCase);

    private async Task<(HttpStatusCode Status, string Body)> GetAsync(string token, string url)
    {
        using var client = fixture.ClientFor(token);
        var response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task A_committee_sees_its_own_rows_and_never_the_others(string template, bool expectsOwnData)
    {
        var a = await GetAsync(fixture.TokenForA, Url(template, fixture.A));
        var b = await GetAsync(fixture.TokenForB, Url(template, fixture.B));

        Assert.True(a.Status == HttpStatusCode.OK, $"A got {(int)a.Status} from {template}: {a.Body}");
        Assert.True(b.Status == HttpStatusCode.OK, $"B got {(int)b.Status} from {template}: {b.Body}");

        Assert.False(Mentions(a.Body, fixture.B), $"A's response from {template} contains committee B's data: {Trim(a.Body)}");
        Assert.False(Mentions(b.Body, fixture.A), $"B's response from {template} contains committee A's data: {Trim(b.Body)}");

        if (expectsOwnData)
        {
            // Without this the assertions above would pass on an empty body.
            Assert.True(Mentions(a.Body, fixture.A), $"A found none of its own data at {template}: {Trim(a.Body)}");
            Assert.True(Mentions(b.Body, fixture.B), $"B found none of its own data at {template}: {Trim(b.Body)}");
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Passing_the_other_committees_year_id_returns_nothing_of_theirs(string template, bool _)
    {
        if (!template.Contains("{year}", StringComparison.Ordinal))
        {
            return;
        }

        // The year id is client-supplied, and a member of one committee can
        // simply type another's. It must not widen what they can read.
        var result = await GetAsync(fixture.TokenForA, Url(template, fixture.B));

        Assert.NotEqual(HttpStatusCode.InternalServerError, result.Status);
        Assert.False(
            result.Body.Contains(fixture.B.Marker, StringComparison.Ordinal)
                || result.Body.Contains(fixture.B.Org.ToString(), StringComparison.OrdinalIgnoreCase),
            $"A, passing B's year id to {template}, received B's data: {Trim(result.Body)}");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task A_session_with_no_committee_selected_is_refused_not_served_empty(string template, bool _)
    {
        // A clean 403: never an empty 200 that reads as "this committee has no
        // data", and never a 500.
        var result = await GetAsync(fixture.TokenUnpinned, Url(template, fixture.A));

        Assert.True(
            result.Status == HttpStatusCode.Forbidden,
            $"Unpinned session got {(int)result.Status} from {template}: {Trim(result.Body)}");
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task A_signed_in_account_with_no_membership_is_refused(string template, bool _)
    {
        var result = await GetAsync(fixture.TokenOutsider, Url(template, fixture.A));

        Assert.True(
            result.Status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,
            $"Outsider got {(int)result.Status} from {template}: {Trim(result.Body)}");
        Assert.False(Mentions(result.Body, fixture.A));
    }

    [Fact]
    public async Task Detail_routes_keyed_by_the_other_committees_ids_return_nothing_of_theirs()
    {
        var probes = new[]
        {
            $"/api/advances/{fixture.B.Advance}/settlements",
            $"/api/contracts/{fixture.B.Contract}/payments",
            $"/api/workflow_items/{fixture.B.WorkflowItem}/notes",
            $"/api/vendor_issues?vendor_id={fixture.B.Vendor}",
            $"/api/vendor_issues?contract_id={fixture.B.Contract}",
        };

        foreach (var probe in probes)
        {
            var result = await GetAsync(fixture.TokenForA, probe);

            Assert.NotEqual(HttpStatusCode.InternalServerError, result.Status);
            Assert.False(Mentions(result.Body, fixture.B), $"A, probing {probe}, received B's data: {Trim(result.Body)}");
        }
    }

    [Fact]
    public async Task The_users_and_access_request_lists_are_per_committee()
    {
        // aonly@ belongs to A alone; the pending request is at A alone.
        var usersA = await GetAsync(fixture.TokenForA, "/api/users");
        var usersB = await GetAsync(fixture.TokenForB, "/api/users");
        var requestsA = await GetAsync(fixture.TokenForA, "/api/access_requests");
        var requestsB = await GetAsync(fixture.TokenForB, "/api/access_requests");

        Assert.Contains("aonly@test.local", usersA.Body);
        Assert.DoesNotContain("aonly@test.local", usersB.Body);
        Assert.Contains("outsider@test.local", requestsA.Body);
        Assert.DoesNotContain("outsider@test.local", requestsB.Body);
    }

    [Fact]
    public async Task Fund_position_figures_do_not_include_the_other_committee()
    {
        // Seeded collected: A 5000, B 7000; spent: A 1000, B 2000. Identical
        // figures would mean both are summing one combined set of rows.
        var a = await GetAsync(fixture.TokenForA, $"/api/fund_position?year_id={fixture.A.Year}");
        var b = await GetAsync(fixture.TokenForB, $"/api/fund_position?year_id={fixture.B.Year}");

        Assert.Equal(HttpStatusCode.OK, a.Status);
        Assert.Equal(HttpStatusCode.OK, b.Status);
        Assert.NotEqual(a.Body, b.Body);
        Assert.DoesNotContain("12000", a.Body);
        Assert.DoesNotContain("12000", b.Body);
    }

    private static string Trim(string body) => body.Length <= 300 ? body : body[..300] + "...";
}
