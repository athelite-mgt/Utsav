namespace Utsav.Infrastructure.Authentication;

public sealed record SessionUser(Guid Id, string Email, string? FullName, string AuthProvider);

public sealed record SessionOrganization(Guid Id, string Name, string Slug);

/// <summary>
/// Mirrors the shape <c>loadSession()</c> returns in <c>server/auth.mjs</c> —
/// used by <c>/auth/me</c> and every auth response. <see cref="Org"/> and
/// <see cref="Role"/> are only populated once <see cref="Status"/> is "active".
/// </summary>
public sealed record SessionPayload(SessionUser User, SessionOrganization? Org, string? Role, string Status);
