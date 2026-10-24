namespace Utsav.Infrastructure.Authentication;

public sealed record SessionUser(Guid Id, string Email, string? FullName, string AuthProvider);

public sealed record SessionOrganization(
    Guid Id, string Name, string Slug, string? City, string? LogoUrl, string? AccentColour);

/// <summary>
/// One committee this user belongs to. The switcher renders this list, so it
/// carries enough branding to show a logo and colour per entry without a
/// second round trip.
/// </summary>
public sealed record SessionMembership(
    Guid OrgId, string Name, string Slug, string? City, string? LogoUrl, string? AccentColour, string Role, string Status);

/// <summary>
/// What <c>/auth/me</c> returns, and what the authentication handler resolves
/// on every request.
///
/// <see cref="Org"/> and <see cref="Role"/> are populated only when the
/// session is pinned to a committee the user is an <em>active</em> member of
/// right now: membership is re-verified on every load, so revoking access
/// takes effect on the member's next request rather than at session expiry.
///
/// <see cref="Memberships"/> is always populated, including when
/// <see cref="Org"/> is null: that is the state a multi-committee user is in
/// before choosing one, and the chooser screen needs the list.
/// </summary>
public sealed record SessionPayload(
    SessionUser User,
    SessionOrganization? Org,
    string? Role,
    string Status,
    IReadOnlyList<SessionMembership> Memberships,
    bool IsPlatformAdmin);
