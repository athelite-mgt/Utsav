using Utsav.Infrastructure.Push;

namespace Utsav.UnitTests;

/// <summary>
/// <see cref="NotifiableTables.TableForRoute"/> is what lets a clicked
/// notification find its way back to the audit entry it came from (see
/// PushEndpoints.GetDetailAsync) — every route in <see cref="NotifiableTables.Meta"/>
/// needs to resolve back to the same table it came from, or a notification's
/// "detail" would silently correlate to the wrong module.
/// </summary>
public class NotifiableTablesTests
{
    [Theory]
    [InlineData("subscriptions", "/membership")]
    [InlineData("ad_bookings", "/ads")]
    [InlineData("special_donations", "/other-incomes")]
    [InlineData("expenses", "/expenses")]
    public void Every_notifiable_table_round_trips_through_its_route(string table, string route)
    {
        Assert.Equal(route, NotifiableTables.Meta[table].Route);
        Assert.Equal(table, NotifiableTables.TableForRoute[route]);
    }

    [Fact]
    public void Unknown_route_is_not_present()
    {
        Assert.False(NotifiableTables.TableForRoute.ContainsKey("/not-a-real-route"));
    }

    [Fact]
    public void A_route_shared_by_two_tables_still_resolves_to_one_of_them()
    {
        // "contracts" and "contract_payments" both point at "/vendors" — which
        // one wins isn't guaranteed, but the map must still contain a valid
        // answer rather than throwing at startup over the duplicate route.
        Assert.True(NotifiableTables.TableForRoute.TryGetValue("/vendors", out var table));
        Assert.Contains(table, new[] { "contracts", "contract_payments" });
    }
}
