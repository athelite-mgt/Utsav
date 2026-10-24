using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Utsav.IsolationTests;

[Collection(nameof(DatabaseFixture))]
public class SmokeTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task The_fixture_boots_the_real_app_against_a_migrated_database()
    {
        using var client = fixture.ClientFor(fixture.TokenForA);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(fixture.TenantA.ToString(), me.GetProperty("org").GetProperty("id").GetString());
    }
}
