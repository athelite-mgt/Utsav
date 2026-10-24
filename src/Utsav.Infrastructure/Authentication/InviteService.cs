using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Authentication;

public enum RedeemOutcome
{
    Redeemed,
    UnknownCode,
    AlreadyUsed,
    Expired,
    WrongAccount,
}

public sealed record RedeemResult(RedeemOutcome Outcome, Guid? OrgId = null);

/// <summary>
/// Issues and redeems the one-time codes that give a committee its first
/// administrator. See <see cref="InviteCodes"/> for why this exists rather than
/// granting admin to whoever registers a given email address.
/// </summary>
public sealed class InviteService(UtsavDbContext db)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    /// <summary>
    /// Adds an invitation to the context (the caller saves it, so it can share a
    /// transaction with creating the committee) and returns the plaintext code.
    /// This is the only moment the plaintext exists on the server.
    /// </summary>
    public string Add(Guid orgId, string email, string role, DateTime nowUtc)
    {
        var code = InviteCodes.Generate();
        db.OrgInvites.Add(new OrgInvite
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            Email = email.Trim().ToLowerInvariant(),
            Role = role,
            CodeHash = InviteCodes.Hash(code),
            ExpiresAt = nowUtc + Lifetime,
        });

        return code;
    }

    public async Task<RedeemResult> RedeemAsync(Guid userId, string userEmail, string? code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new RedeemResult(RedeemOutcome.UnknownCode);
        }

        var hash = InviteCodes.Hash(code);
        var invite = await db.OrgInvites.AsNoTracking().SingleOrDefaultAsync(i => i.CodeHash == hash, ct);
        if (invite is null)
        {
            return new RedeemResult(RedeemOutcome.UnknownCode);
        }

        if (invite.RedeemedAt is not null)
        {
            return new RedeemResult(RedeemOutcome.AlreadyUsed);
        }

        var now = DateTime.UtcNow;
        if (invite.ExpiresAt <= now)
        {
            return new RedeemResult(RedeemOutcome.Expired);
        }

        // Checked BEFORE the code is spent, so someone else trying a code they
        // came across cannot use it up and lock the real person out.
        if (!string.Equals(invite.Email, userEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new RedeemResult(RedeemOutcome.WrongAccount);
        }

        var outcome = RedeemOutcome.Redeemed;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // The claim is a conditional update, so two simultaneous redemptions
            // cannot both win: exactly one sees a row affected.
            var claimed = await db.OrgInvites
                .Where(i => i.Id == invite.Id && i.RedeemedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.RedeemedAt, now)
                    .SetProperty(i => i.RedeemedByUserId, userId), ct);
            if (claimed == 0)
            {
                outcome = RedeemOutcome.AlreadyUsed;
                return;
            }

            var membership = await db.OrgMembers.AsTracking()
                .SingleOrDefaultAsync(m => m.OrgId == invite.OrgId && m.UserId == userId, ct);
            if (membership is null)
            {
                db.OrgMembers.Add(new OrgMember
                {
                    Id = Guid.NewGuid(), OrgId = invite.OrgId, UserId = userId, Role = invite.Role, Status = "active",
                });
            }
            else
            {
                // They had already asked to join: the invitation settles it.
                membership.Role = invite.Role;
                membership.Status = "active";
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

        return new RedeemResult(outcome, outcome == RedeemOutcome.Redeemed ? invite.OrgId : null);
    }
}
