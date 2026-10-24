using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Utsav.Infrastructure.Data;

/// <summary>
/// Makes the very first platform operator, since nobody can promote them until
/// one exists. Driven by configuration (<c>Platform:BootstrapAdminEmail</c>) and
/// deliberately inert once any operator exists, so leaving the setting in place
/// can never quietly re-grant access that was deliberately revoked.
/// </summary>
public sealed class PlatformBootstrapper(UtsavDbContext db, ILogger<PlatformBootstrapper> logger)
{
    public async Task<bool> EnsureAsync(string? email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        if (await db.Users.AnyAsync(u => u.IsPlatformAdmin, ct))
        {
            return false;
        }

        var wanted = email.Trim();
        var user = await db.Users.AsTracking().FirstOrDefaultAsync(u => u.Email == wanted, ct);
        if (user is null)
        {
            logger.LogWarning(
                "Platform:BootstrapAdminEmail is {Email} but no such account exists yet. Sign up with that address, then restart.",
                wanted);
            return false;
        }

        user.IsPlatformAdmin = true;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Promoted {Email} to platform operator", wanted);
        return true;
    }
}
