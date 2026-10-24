using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;
using Xunit;

namespace Utsav.UnitTests.Authentication;

public class SessionTenantResolutionTests
{
    private static UtsavDbContext InMemory(string name) =>
        new(new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(name).Options);

    /// <summary>
    /// A session that has not expired. The in-memory provider does not apply the
    /// column defaults SQL Server does, so the lifetime has to be stated here.
    /// </summary>
    private static Session Live(string rawToken, Guid userId, Guid? orgId) => new()
    {
        Token = SessionTokens.Hash(rawToken),
        UserId = userId,
        OrgId = orgId,
        ExpiresAt = DateTime.UtcNow.AddDays(10),
        LastSeenAt = DateTime.UtcNow,
    };

    private static async Task<(UtsavDbContext Db, Guid UserId, Guid OrgA, Guid OrgB)> SeedAsync(string name)
    {
        var db = InMemory(name);
        var userId = Guid.NewGuid();
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        db.Users.Add(new User { Id = userId, Email = "a@b.c", AuthProvider = "local", PasswordHash = "x", Role = "viewer" });
        db.Organizations.Add(new Organization { Id = orgA, Name = "Durga", Slug = "durga", IsActive = true });
        db.Organizations.Add(new Organization { Id = orgB, Name = "Kali", Slug = "kali", IsActive = true });
        await db.SaveChangesAsync();
        return (db, userId, orgA, orgB);
    }

    [Fact]
    public async Task Session_with_null_org_resolves_no_tenant_but_lists_memberships()
    {
        var (db, userId, orgA, orgB) = await SeedAsync(nameof(Session_with_null_org_resolves_no_tenant_but_lists_memberships));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgB, UserId = userId, Role = "viewer", Status = "active" });
        db.Sessions.Add(Live("t1", userId, null));
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t1");

        Assert.NotNull(session);
        Assert.Null(session!.Org);
        Assert.Equal(2, session.Memberships.Count);
        Assert.Equal("active", session.Status);
    }

    [Fact]
    public async Task Session_pinned_to_an_org_resolves_that_org_and_its_role()
    {
        var (db, userId, orgA, _) = await SeedAsync(nameof(Session_pinned_to_an_org_resolves_that_org_and_its_role));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "finance", Status = "active" });
        db.Sessions.Add(Live("t2", userId, orgA));
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t2");

        Assert.Equal(orgA, session!.Org!.Id);
        Assert.Equal("finance", session.Role);
    }

    [Fact]
    public async Task Membership_revoked_after_sign_in_stops_working_immediately()
    {
        // The session row still names the org, but the membership is gone.
        // The next request must not see that org's data.
        var (db, userId, orgA, _) = await SeedAsync(nameof(Membership_revoked_after_sign_in_stops_working_immediately));
        var member = new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" };
        db.OrgMembers.Add(member);
        db.Sessions.Add(Live("t3", userId, orgA));
        await db.SaveChangesAsync();

        db.OrgMembers.Remove(member);
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t3");

        Assert.NotNull(session);
        Assert.Null(session!.Org);
        Assert.Null(session.Role);
        Assert.NotEqual("active", session.Status);
    }

    [Fact]
    public async Task Session_pinned_to_an_org_the_user_only_has_pending_access_to_is_not_active()
    {
        var (db, userId, orgA, _) = await SeedAsync(nameof(Session_pinned_to_an_org_the_user_only_has_pending_access_to_is_not_active));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "viewer", Status = "pending" });
        db.Sessions.Add(Live("t4", userId, orgA));
        await db.SaveChangesAsync();

        var session = await new SessionService(db).LoadSessionAsync("t4");

        Assert.Null(session!.Org);
        Assert.Equal("pending", session.Status);
    }

    [Fact]
    public async Task SwitchOrg_refuses_an_org_the_user_is_not_an_active_member_of()
    {
        var (db, userId, orgA, orgB) = await SeedAsync(nameof(SwitchOrg_refuses_an_org_the_user_is_not_an_active_member_of));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        db.Sessions.Add(Live("t5", userId, orgA));
        await db.SaveChangesAsync();
        var sessions = new SessionService(db);

        // No membership in orgB at all: the switch must be refused and the
        // session must stay pinned where it was.
        Assert.False(await sessions.SwitchOrgAsync(userId, orgB, "t5"));
        Assert.Equal(orgA, (await db.Sessions.SingleAsync(s => s.Token == SessionTokens.Hash("t5"))).OrgId);
    }

    [Fact]
    public async Task SwitchOrg_moves_the_session_to_an_org_the_user_belongs_to()
    {
        var (db, userId, orgA, orgB) = await SeedAsync(nameof(SwitchOrg_moves_the_session_to_an_org_the_user_belongs_to));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgB, UserId = userId, Role = "viewer", Status = "active" });
        db.Sessions.Add(Live("t6", userId, orgA));
        await db.SaveChangesAsync();

        Assert.True(await new SessionService(db).SwitchOrgAsync(userId, orgB, "t6"));
        Assert.Equal(orgB, (await db.Sessions.SingleAsync(s => s.Token == SessionTokens.Hash("t6"))).OrgId);
    }

    [Fact]
    public async Task A_single_active_membership_pins_the_session_automatically()
    {
        // The common case: showing a one-item chooser would be worse than
        // just going in.
        var (db, userId, orgA, _) = await SeedAsync(nameof(A_single_active_membership_pins_the_session_automatically));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        await db.SaveChangesAsync();

        var token = await new SessionService(db).CreateSessionAsync(userId);

        Assert.Equal(orgA, (await db.Sessions.SingleAsync(s => s.Token == SessionTokens.Hash(token))).OrgId);
    }

    [Fact]
    public async Task Several_active_memberships_leave_the_session_unpinned_for_the_chooser()
    {
        var (db, userId, orgA, orgB) = await SeedAsync(nameof(Several_active_memberships_leave_the_session_unpinned_for_the_chooser));
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgA, UserId = userId, Role = "admin", Status = "active" });
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = orgB, UserId = userId, Role = "viewer", Status = "active" });
        await db.SaveChangesAsync();

        var token = await new SessionService(db).CreateSessionAsync(userId);

        Assert.Null((await db.Sessions.SingleAsync(s => s.Token == SessionTokens.Hash(token))).OrgId);
    }
}
