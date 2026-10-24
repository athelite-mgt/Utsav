using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data.Entities;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// The inherited multi-step features, run end to end inside a committee. The
/// isolation tests prove nothing leaks; these prove the guards added to stop
/// leaks have not broken the features themselves. All run in committee C so
/// they can change data without disturbing the read and leak tests.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class FlowTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string> BodyAsync(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    [Fact]
    public async Task Year_rollover_creates_the_next_year_for_this_committee_only()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var response = await asC.PostAsJsonAsync($"/api/puja_years/{fixture.C.Year}/rollover", new { year = 2027, name = "Flow Puja 2027" });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await BodyAsync(response));

        await using var inC = fixture.ContextFor(fixture.TenantC);
        var years = await inC.PujaYears.OrderBy(y => y.Year).ToListAsync();
        Assert.Equal([2026, 2027], years.Select(y => y.Year));
        Assert.Equal("Flow Puja 2027", years[1].Name);

        // The prior year's subscription was carried into the new one, filed
        // under the new year in the same transaction.
        Assert.True(await inC.Subscriptions.AnyAsync(s => s.YearId == years[1].Id));

        await using var inA = fixture.ContextFor(fixture.TenantA);
        await using var inB = fixture.ContextFor(fixture.TenantB);
        Assert.Single(await inA.PujaYears.ToListAsync());
        Assert.Single(await inB.PujaYears.ToListAsync());
    }

    [Fact]
    public async Task A_contract_can_be_created_and_paid_in_instalments()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var created = await asC.PostAsJsonAsync("/api/contracts", new
        {
            vendor_id = fixture.C.Vendor, year_id = fixture.C.Year, title = "Flow contract",
            contract_amount = 5000, category = "Decoration", status = "active",
        });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await BodyAsync(created));
        var contractId = (await JsonAsync(created)).GetProperty("id").GetGuid();

        var payment = await asC.PostAsJsonAsync("/api/contract_payments", new
        {
            contract_id = contractId, amount = 100, milestone = "advance", due_date = "2026-10-10",
        });
        Assert.True(payment.StatusCode == HttpStatusCode.OK, await BodyAsync(payment));

        var listed = await BodyAsync(await asC.GetAsync($"/api/contracts/{contractId}/payments"));
        Assert.Contains("advance", listed);
    }

    [Fact]
    public async Task An_advance_can_be_issued_and_settled_against_an_expense()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var issued = await asC.PostAsJsonAsync("/api/advances", new
        {
            issued_to_name = "Flow holder", year_id = fixture.C.Year, amount = 500, issued_on = "2026-10-04", purpose = "flow",
        });
        Assert.True(issued.StatusCode == HttpStatusCode.OK, await BodyAsync(issued));
        var advanceId = (await JsonAsync(issued)).GetProperty("id").GetGuid();

        var settled = await asC.PostAsJsonAsync($"/api/advances/{advanceId}/settle", new
        {
            kind = "expense", amount = 200, category = "Decoration", details = "settled flow",
        });
        Assert.True(settled.StatusCode == HttpStatusCode.OK, await BodyAsync(settled));

        await using var inC = fixture.ContextFor(fixture.TenantC);
        var balance = await inC.VAdvanceBalances.SingleAsync(b => b.Id == advanceId);
        Assert.Equal(300m, balance.Outstanding);
        Assert.True(await inC.Expenses.AnyAsync(e => e.Details == "settled flow"));
    }

    [Fact]
    public async Task A_workflow_item_takes_notes_and_a_vendor_issue_can_be_logged()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var note = await asC.PostAsJsonAsync($"/api/workflow_items/{fixture.C.WorkflowItem}/notes", new { note = "flow note" });
        Assert.True(note.StatusCode == HttpStatusCode.OK, await BodyAsync(note));
        Assert.Contains("flow note", await BodyAsync(await asC.GetAsync($"/api/workflow_items/{fixture.C.WorkflowItem}/notes")));

        var issue = await asC.PostAsJsonAsync("/api/vendor_issues", new
        {
            vendor_id = fixture.C.Vendor, issue_type = "delay", description = "delivered late",
        });
        Assert.True(issue.StatusCode == HttpStatusCode.OK, await BodyAsync(issue));
        Assert.Contains("delivered late", await BodyAsync(await asC.GetAsync($"/api/vendor_issues?vendor_id={fixture.C.Vendor}")));
    }

    [Fact]
    public async Task Feast_tokens_can_be_printed_and_looked_up_only_within_their_committee()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);
        using var asA = fixture.ClientFor(fixture.TokenForA);

        var print = await asC.PostAsJsonAsync("/api/feast_tokens/print", new { year_id = fixture.C.Year.ToString(), count = 3 });
        Assert.True(print.StatusCode == HttpStatusCode.OK, await BodyAsync(print));

        var list = await JsonAsync(await asC.GetAsync($"/api/feast_tokens?year_id={fixture.C.Year}"));
        Assert.Equal(3, list.GetArrayLength());
        var code = list[0].GetProperty("code").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await asC.GetAsync($"/api/feast_tokens/lookup/{code}")).StatusCode);

        // The same code means nothing to another committee.
        var foreign = await asA.GetAsync($"/api/feast_tokens/lookup/{code}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(0, (await JsonAsync(await asA.GetAsync($"/api/feast_tokens?year_id={fixture.A.Year}"))).GetArrayLength());
    }

    [Fact]
    public async Task Two_committees_can_print_feast_tokens_with_the_same_codes()
    {
        // Token codes follow a per-year pattern, so two committees printing for
        // the same year produce the same codes. A globally unique index on code
        // would make the second committee's print fail.
        using var asB = fixture.ClientFor(fixture.TokenForB);
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var printB = await asB.PostAsJsonAsync("/api/feast_tokens/print", new { year_id = fixture.B.Year.ToString(), count = 2 });
        var printC = await asC.PostAsJsonAsync("/api/feast_tokens/print", new { year_id = fixture.C.Year.ToString(), count = 2 });
        Assert.True(printB.StatusCode == HttpStatusCode.OK, "B: " + await BodyAsync(printB));
        Assert.True(printC.StatusCode == HttpStatusCode.OK, "C: " + await BodyAsync(printC));

        var codesB = (await JsonAsync(await asB.GetAsync($"/api/feast_tokens?year_id={fixture.B.Year}")))
            .EnumerateArray().Select(t => t.GetProperty("code").GetString()).ToHashSet();
        var codesC = (await JsonAsync(await asC.GetAsync($"/api/feast_tokens?year_id={fixture.C.Year}")))
            .EnumerateArray().Select(t => t.GetProperty("code").GetString()).ToHashSet();

        Assert.NotEmpty(codesB);
        Assert.True(codesB.Overlaps(codesC), "expected the two committees to have been issued overlapping codes");

        // A shared code resolves to each committee's own token, never the other's.
        var shared = codesB.Intersect(codesC).First()!;
        var lookB = await JsonAsync(await asB.GetAsync($"/api/feast_tokens/lookup/{shared}"));
        var lookC = await JsonAsync(await asC.GetAsync($"/api/feast_tokens/lookup/{shared}"));
        Assert.DoesNotContain(fixture.C.Year.ToString(), lookB.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.B.Year.ToString(), lookC.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_api_key_serves_only_its_own_committees_audit_report_and_nothing_else()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var created = await asC.PostAsJsonAsync("/api/api_keys", new { name = "flow agent" });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await BodyAsync(created));
        var key = (await JsonAsync(created)).GetProperty("key").GetString()!;

        using var agent = fixture.AnonymousClient();
        agent.DefaultRequestHeaders.Add("X-Api-Key", key);

        var own = await agent.GetAsync($"/api/reports/audit.pdf?year_id={fixture.C.Year}");
        Assert.True(own.StatusCode == HttpStatusCode.OK, await BodyAsync(own));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await own.Content.ReadAsByteArrayAsync()).AsSpan(0, 4)));

        // The key belongs to C. Another committee's year id is simply not found.
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"/api/reports/audit.pdf?year_id={fixture.A.Year}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync($"/api/reports/audit.pdf?year_id={fixture.B.Year}")).StatusCode);

        // A key reads the report and does nothing else.
        Assert.True((await agent.GetAsync("/api/zones")).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.True((await agent.PostAsJsonAsync("/api/vendors", new { name = "by key" })).StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        using var bogus = fixture.AnonymousClient();
        bogus.DefaultRequestHeaders.Add("X-Api-Key", "utsav_sk_not-a-real-key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await bogus.GetAsync($"/api/reports/audit.pdf?year_id={fixture.C.Year}")).StatusCode);
    }

    [Fact]
    public async Task An_admin_can_only_approve_and_change_members_of_their_own_committee()
    {
        var newcomer = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        await using (var identity = fixture.ContextWithNoTenant())
        {
            identity.Users.Add(new User
            {
                Id = newcomer, Email = $"newcomer-{newcomer:N}@test.local", FullName = "Newcomer", AuthProvider = "local",
                PasswordHash = "!", Role = "viewer",
            });
            identity.OrgMembers.Add(new OrgMember { Id = memberId, OrgId = fixture.TenantC, UserId = newcomer, Role = "viewer", Status = "pending" });
            await identity.SaveChangesAsync();
        }

        // Another committee's admin cannot approve C's request.
        using var asA = fixture.ClientFor(fixture.TokenForA);
        var foreign = await asA.PostAsJsonAsync($"/api/access_requests/{memberId}/approve", new { role = "admin" });
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        await using (var check = fixture.ContextWithNoTenant())
        {
            Assert.Equal("pending", (await check.OrgMembers.SingleAsync(m => m.Id == memberId)).Status);
        }

        // C's own admin can.
        using var asC = fixture.ClientFor(fixture.TokenForC);
        var own = await asC.PostAsJsonAsync($"/api/access_requests/{memberId}/approve", new { role = "viewer" });
        Assert.True(own.StatusCode == HttpStatusCode.OK, await BodyAsync(own));

        // And A's admin cannot then change that member's role.
        var promote = await asA.PatchAsJsonAsync($"/api/users/{newcomer}", new { role = "admin" });
        Assert.Equal(HttpStatusCode.NotFound, promote.StatusCode);

        await using var after = fixture.ContextWithNoTenant();
        var final = await after.OrgMembers.SingleAsync(m => m.Id == memberId);
        Assert.Equal("active", final.Status);
        Assert.Equal("viewer", final.Role);
    }

    [Fact]
    public async Task A_device_subscribed_under_two_committees_is_unsubscribed_from_only_one()
    {
        var endpoint = $"https://push.example/{Guid.NewGuid():N}";
        var body = new { endpoint, keys = new { p256dh = "p", auth = "a" } };

        using var asC = fixture.ClientFor(fixture.TokenForC);
        using var asA = fixture.ClientFor(fixture.TokenForA);

        Assert.Equal(HttpStatusCode.OK, (await asC.PostAsJsonAsync("/api/push/subscribe", body)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await asA.PostAsJsonAsync("/api/push/subscribe", body)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await asC.PostAsJsonAsync("/api/push/unsubscribe", new { endpoint })).StatusCode);

        await using var inC = fixture.ContextFor(fixture.TenantC);
        await using var inA = fixture.ContextFor(fixture.TenantA);
        Assert.False(await inC.PushSubscriptionRecords.AnyAsync(p => p.Endpoint == endpoint));
        Assert.True(await inA.PushSubscriptionRecords.AnyAsync(p => p.Endpoint == endpoint));
    }

    [Fact]
    public async Task Settings_lookups_and_committee_name_can_be_changed_by_that_committees_admin()
    {
        using var asC = fixture.ClientFor(fixture.TokenForC);

        var added = await asC.PostAsJsonAsync("/api/lookups", new { category = "payment_mode", label = "Cheque ZZC", sort_order = 9, active = true });
        Assert.True(added.StatusCode == HttpStatusCode.OK, await BodyAsync(added));
        var lookupId = (await JsonAsync(added)).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await asC.DeleteAsync($"/api/lookups/{lookupId}")).StatusCode);

        var renamed = await asC.PatchAsJsonAsync("/api/organizations/current", new { name = "Flow Committee Renamed" });
        Assert.True(renamed.StatusCode == HttpStatusCode.OK, await BodyAsync(renamed));

        await using var identity = fixture.ContextWithNoTenant();
        Assert.Equal("Flow Committee Renamed", (await identity.Organizations.SingleAsync(o => o.Id == fixture.TenantC)).Name);
        // Renaming C must not rename anyone else.
        Assert.Equal("Durga Committee", (await identity.Organizations.SingleAsync(o => o.Id == fixture.TenantA)).Name);
        Assert.Equal("Kali Committee", (await identity.Organizations.SingleAsync(o => o.Id == fixture.TenantB)).Name);
    }
}
