using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Utsav.Domain.Tenancy;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data.Entities;
using Utsav.Infrastructure.Tenancy;

namespace Utsav.Infrastructure.Data;

public sealed record ProvisionRequest(
    string Name, string? Slug, string? City, string Festival, int FirstYear, string FirstAdminEmail);

public sealed record ProvisionResult(Guid OrgId, string Slug, string InviteCode, DateTime InviteExpiresAt);

public sealed class SlugTakenException(string slug)
    : InvalidOperationException($"A committee with the short name '{slug}' already exists.");

public sealed class InvalidProvisionRequestException(string message) : InvalidOperationException(message);

/// <summary>
/// Creates a committee and everything it needs to be usable on day one.
///
/// Replaces the single-committee bootstrap, which assumed one committee per
/// database and a fixed organization id. Here the id is generated, so there is
/// no single-tenant identity to collide with.
///
/// Everything is written from a scope whose tenant IS the new committee, in one
/// transaction. The tenant has to be established before the first write: the
/// row-level-security BLOCK predicates would otherwise reject the committee's
/// own rows, and a committee that exists with no reference data or no
/// administrator is worse than one that was never created.
///
/// The first administrator is not created here. The committee is handed a
/// one-time invite code for their email address instead; see
/// <see cref="InviteCodes"/> for why.
/// </summary>
public sealed partial class TenantProvisioner(IServiceScopeFactory scopes)
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailShape();

    /// <summary>A sanity check on shape, not proof of ownership: that is what the invite code is for.</summary>
    public static bool IsPlausibleEmail(string? email) =>
        email is { Length: > 0 and <= 254 } && EmailShape().IsMatch(email.Trim());

    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct = default)
    {
        var name = request.Name?.Trim() ?? "";
        if (name.Length is 0 or > 120)
        {
            throw new InvalidProvisionRequestException("A committee name of 1 to 120 characters is required.");
        }

        var email = request.FirstAdminEmail?.Trim() ?? "";
        if (!IsPlausibleEmail(email))
        {
            throw new InvalidProvisionRequestException("A valid email address for the first administrator is required.");
        }

        string slug;
        try
        {
            slug = Slug.From(string.IsNullOrWhiteSpace(request.Slug) ? name : request.Slug);
        }
        catch (ArgumentException)
        {
            throw new InvalidProvisionRequestException("The committee name needs at least one letter or digit.");
        }

        var template = FestivalTemplates.For(request.Festival ?? "");
        var orgId = Guid.NewGuid();

        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Resolve(orgId);
        var db = scope.ServiceProvider.GetRequiredService<UtsavDbContext>();
        var invites = scope.ServiceProvider.GetRequiredService<InviteService>();

        if (await db.Organizations.AnyAsync(o => o.Slug == slug, ct))
        {
            throw new SlugTakenException(slug);
        }

        try
        {
            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                // A retry re-runs this whole delegate, so start from a clean slate.
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                var now = DateTime.UtcNow;
                db.Organizations.Add(new Organization { Id = orgId, Name = name, Slug = slug, City = request.City?.Trim(), IsActive = true });

                db.Zones.AddRange(FestivalTemplates.DefaultZones.Select((zone, i) => new Zone
                {
                    Id = Guid.NewGuid(), OrgId = orgId, Name = zone, SortOrder = i + 1,
                }));

                var lookups = template.Lookups.Append((Category: "puja_day", Labels: template.PujaDays));
                db.Lookups.AddRange(lookups.SelectMany(group => group.Labels.Select((label, i) => new Lookup
                {
                    Id = Guid.NewGuid(), OrgId = orgId, Category = group.Category, Label = label, SortOrder = i + 1, Active = true,
                })));

                db.PujaYears.Add(new PujaYear
                {
                    Id = Guid.NewGuid(), OrgId = orgId, Year = request.FirstYear, Name = $"{template.DisplayName} {request.FirstYear}",
                    Target = 0, ProjectionPct = 6.50m, IsActive = true,
                });

                // No collectors, no donors: those are a committee's own people.

                var code = invites.Add(orgId, email, "admin", now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return new ProvisionResult(orgId, slug, code, now + InviteService.Lifetime);
            });
        }
        catch (DbUpdateException)
        {
            // Most likely lost a race with another operator creating the same
            // short name between the check above and the insert.
            if (await SlugExistsAsync(scope, slug, ct))
            {
                throw new SlugTakenException(slug);
            }

            throw;
        }
    }

    private static async Task<bool> SlugExistsAsync(IServiceScope scope, string slug, CancellationToken ct)
    {
        await using var check = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        return await check.ServiceProvider.GetRequiredService<UtsavDbContext>().Organizations.AnyAsync(o => o.Slug == slug, ct);
    }
}
