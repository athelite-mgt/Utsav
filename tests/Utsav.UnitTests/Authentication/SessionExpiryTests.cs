using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Authentication;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.UnitTests.Authentication;

public class SessionExpiryTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static async Task<(UtsavDbContext Db, SessionService Sessions, Clock Clock, string Raw, Guid Org)> NewAsync(string name)
    {
        var db = new UtsavDbContext(new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(name).Options);
        var clock = new Clock(Start);
        var userId = Guid.NewGuid();
        var org = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, Email = "a@b.c", AuthProvider = "local", PasswordHash = "x", Role = "viewer" });
        db.Organizations.Add(new Organization { Id = org, Name = "Org", Slug = "org", IsActive = true });
        db.OrgMembers.Add(new OrgMember { Id = Guid.NewGuid(), OrgId = org, UserId = userId, Role = "admin", Status = "active" });
        await db.SaveChangesAsync();

        var sessions = new SessionService(db, clock);
        var raw = await sessions.CreateSessionAsync(userId);
        return (db, sessions, clock, raw, org);
    }

    [Fact]
    public async Task A_fresh_session_loads()
    {
        var (_, sessions, _, raw, org) = await NewAsync(nameof(A_fresh_session_loads));

        var loaded = await sessions.LoadSessionAsync(raw);

        Assert.NotNull(loaded);
        Assert.Equal(org, loaded!.Org!.Id);
    }

    [Fact]
    public async Task The_database_never_holds_the_token_the_browser_holds()
    {
        var (db, _, _, raw, _) = await NewAsync(nameof(The_database_never_holds_the_token_the_browser_holds));

        Assert.False(await db.Sessions.AnyAsync(s => s.Token == raw));
        Assert.True(await db.Sessions.AnyAsync(s => s.Token == SessionTokens.Hash(raw)));
    }

    [Fact]
    public async Task A_session_dies_after_the_absolute_lifetime_however_active_it_has_been()
    {
        var (db, sessions, clock, raw, _) = await NewAsync(nameof(A_session_dies_after_the_absolute_lifetime_however_active_it_has_been));

        // Used every few days, so never idle - but a stolen token must still have a bounded life.
        for (var day = 5; day < SessionService.AbsoluteLifetime.TotalDays; day += 5)
        {
            clock.Now = Start.AddDays(day);
            Assert.NotNull(await sessions.LoadSessionAsync(raw));
        }

        clock.Now = Start + SessionService.AbsoluteLifetime + TimeSpan.FromMinutes(1);

        Assert.Null(await sessions.LoadSessionAsync(raw));
        Assert.False(await db.Sessions.AnyAsync(), "an expired session row should be deleted, not left lying around");
    }

    [Fact]
    public async Task A_session_dies_when_it_has_been_idle_too_long_even_if_its_lifetime_is_not_up()
    {
        var (_, sessions, clock, raw, _) = await NewAsync(nameof(A_session_dies_when_it_has_been_idle_too_long_even_if_its_lifetime_is_not_up));

        clock.Now = Start + SessionService.IdleTimeout + TimeSpan.FromMinutes(1);

        Assert.Null(await sessions.LoadSessionAsync(raw));
    }

    [Fact]
    public async Task Using_a_session_keeps_it_alive_past_the_idle_timeout()
    {
        var (_, sessions, clock, raw, _) = await NewAsync(nameof(Using_a_session_keeps_it_alive_past_the_idle_timeout));

        clock.Now = Start.AddDays(10);
        Assert.NotNull(await sessions.LoadSessionAsync(raw));

        // Twenty days after creation: past the idle timeout measured from the
        // start, but only ten days since it was last used.
        clock.Now = Start.AddDays(20);
        Assert.NotNull(await sessions.LoadSessionAsync(raw));
    }

    [Fact]
    public async Task Activity_is_recorded_at_most_every_few_minutes_not_on_every_request()
    {
        var (db, sessions, clock, raw, _) = await NewAsync(nameof(Activity_is_recorded_at_most_every_few_minutes_not_on_every_request));
        var hash = SessionTokens.Hash(raw);

        clock.Now = Start.AddMinutes(1);
        await sessions.LoadSessionAsync(raw);
        var afterFirst = (await db.Sessions.SingleAsync(s => s.Token == hash)).LastSeenAt;

        clock.Now = Start.AddMinutes(2);
        await sessions.LoadSessionAsync(raw);
        Assert.Equal(afterFirst, (await db.Sessions.SingleAsync(s => s.Token == hash)).LastSeenAt);

        clock.Now = Start.AddMinutes(30);
        await sessions.LoadSessionAsync(raw);
        Assert.True((await db.Sessions.SingleAsync(s => s.Token == hash)).LastSeenAt > afterFirst);
    }

    [Fact]
    public async Task Signing_out_by_the_browsers_token_removes_the_session()
    {
        var (db, sessions, _, raw, _) = await NewAsync(nameof(Signing_out_by_the_browsers_token_removes_the_session));

        await sessions.DestroySessionAsync(raw);

        Assert.False(await db.Sessions.AnyAsync());
        Assert.Null(await sessions.LoadSessionAsync(raw));
    }

    [Fact]
    public async Task The_stored_hash_cannot_itself_be_presented_as_a_session()
    {
        var (_, sessions, _, raw, _) = await NewAsync(nameof(The_stored_hash_cannot_itself_be_presented_as_a_session));

        // What a database leak would reveal must not be a working credential.
        Assert.Null(await sessions.LoadSessionAsync(SessionTokens.Hash(raw)));
    }
}
