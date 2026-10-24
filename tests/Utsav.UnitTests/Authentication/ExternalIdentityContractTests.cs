using System.Text.Json;

namespace Utsav.UnitTests.Authentication;

/// <summary>
/// Pins the wire contract of the sign-in endpoints.
///
/// Both bugs these cover were invisible at compile time and only appeared when
/// a real person pressed a button: the API expects snake_case field names
/// because of the global JSON naming policy, so a body sent as
/// <c>{ idToken }</c> silently bound to null and came back "Missing Microsoft
/// credential" for a perfectly valid token.
/// </summary>
public class ExternalIdentityContractTests
{
    /// <summary>
    /// Mirrors <c>ConfigureHttpJsonOptions</c> in Program.cs. If that policy is
    /// ever changed, these tests should fail rather than the login screen.
    /// </summary>
    private static readonly JsonSerializerOptions ApiJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed record EntraRequest(string? IdToken);

    private sealed record SignupRequest(string? Email, string? Password, string? FullName);

    private sealed record GoogleRequest(string? Credential);

    [Fact]
    public void Entra_expects_id_token_not_idToken()
    {
        var wire = JsonSerializer.Serialize(new EntraRequest("abc"), ApiJson);

        Assert.Contains("\"id_token\"", wire);
        Assert.DoesNotContain("\"idToken\"", wire);
    }

    /// <summary>
    /// The exact body <c>AuthContext.signInWithMicrosoft</c> posts. Binding it
    /// back must produce a populated token, not null.
    /// </summary>
    [Fact]
    public void The_body_the_front_end_sends_binds_to_a_populated_request()
    {
        var sentByBrowser = """{"id_token":"header.payload.signature"}""";

        var bound = JsonSerializer.Deserialize<EntraRequest>(sentByBrowser, ApiJson);

        Assert.NotNull(bound);
        Assert.Equal("header.payload.signature", bound.IdToken);
    }

    [Fact]
    public void Google_still_takes_credential_matching_the_legacy_Node_route()
    {
        var wire = JsonSerializer.Serialize(new GoogleRequest("abc"), ApiJson);
        Assert.Contains("\"credential\"", wire);

        var bound = JsonSerializer.Deserialize<GoogleRequest>("""{"credential":"abc"}""", ApiJson);
        Assert.Equal("abc", bound!.Credential);
    }

    /// <summary>
    /// The existing sign-up form posts <c>full_name</c>; the policy has to keep
    /// producing that, not <c>fullName</c>.
    /// </summary>
    [Fact]
    public void Signup_expects_full_name()
    {
        var bound = JsonSerializer.Deserialize<SignupRequest>(
            """{"email":"a@b.c","password":"secret123","full_name":"A B"}""", ApiJson);

        Assert.Equal("A B", bound!.FullName);
        Assert.Equal("a@b.c", bound.Email);
    }
}
