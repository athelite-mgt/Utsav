using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Utsav.IsolationTests;

/// <summary>
/// The sign-in page needs the identity providers' client IDs before anyone is signed in. They are
/// public by nature (they are in every token), and serving them from the running app means the
/// deployment's configuration is the single source, with no value baked into the build.
/// </summary>
[Collection(nameof(DatabaseFixture))]
public class AuthConfigTests(DatabaseFixture fixture)
{
    private static async Task<JsonElement> GetConfigAsync(WebApplicationFactory<Program> host)
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.GetAsync("/api/auth/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Configured_client_ids_are_served_without_signing_in()
    {
        await using var host = fixture.NewHost(b =>
        {
            b.UseSetting("Google:ClientId", "google-id.apps.example");
            b.UseSetting("AzureAd:ClientId", "entra-client");
            b.UseSetting("AzureAd:TenantId", "entra-tenant");
        });

        var config = await GetConfigAsync(host);

        Assert.Equal("google-id.apps.example", config.GetProperty("google_client_id").GetString());
        Assert.Equal("entra-client", config.GetProperty("entra_client_id").GetString());
        Assert.Equal("entra-tenant", config.GetProperty("entra_tenant_id").GetString());
    }

    [Fact]
    public async Task Unconfigured_providers_are_reported_as_absent_not_as_empty_strings()
    {
        await using var host = fixture.NewHost(b =>
        {
            b.UseSetting("Google:ClientId", "");
            b.UseSetting("AzureAd:ClientId", "");
            b.UseSetting("AzureAd:TenantId", "");
        });

        var config = await GetConfigAsync(host);

        Assert.Equal(JsonValueKind.Null, config.GetProperty("google_client_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, config.GetProperty("entra_client_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, config.GetProperty("entra_tenant_id").ValueKind);
    }

    [Fact]
    public async Task Nothing_secret_is_ever_in_the_response()
    {
        await using var host = fixture.NewHost(b =>
        {
            b.UseSetting("Google:ClientId", "gid");
            b.UseSetting("Google:ClientSecret", "TOP-SECRET-VALUE");
            b.UseSetting("Cosmos:Key", "COSMOS-SECRET");
        });

        using var client = host.CreateClient();
        var body = await (await client.GetAsync("/api/auth/config")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("TOP-SECRET-VALUE", body);
        Assert.DoesNotContain("COSMOS-SECRET", body);
        Assert.Equal(new[] { "entra_client_id", "entra_tenant_id", "google_client_id" },
            JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
    }
}
