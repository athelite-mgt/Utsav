using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.UnitTests.Provisioning;

public class PlatformBootstrapperTests
{
    private static UtsavDbContext NewDb() =>
        new(new DbContextOptionsBuilder<UtsavDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static User Account(string email, bool admin = false) => new()
    {
        Id = Guid.NewGuid(), Email = email, AuthProvider = "local", PasswordHash = "!", Role = "viewer", IsPlatformAdmin = admin,
    };

    private static PlatformBootstrapper For(UtsavDbContext db) => new(db, NullLogger<PlatformBootstrapper>.Instance);

    [Fact]
    public async Task The_named_account_becomes_the_first_operator()
    {
        await using var db = NewDb();
        db.Users.Add(Account("boss@example.org"));
        await db.SaveChangesAsync();

        Assert.True(await For(db).EnsureAsync("boss@example.org"));

        Assert.True((await db.Users.SingleAsync()).IsPlatformAdmin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task With_no_setting_nobody_is_promoted(string? email)
    {
        await using var db = NewDb();
        db.Users.Add(Account("boss@example.org"));
        await db.SaveChangesAsync();

        Assert.False(await For(db).EnsureAsync(email));

        Assert.False((await db.Users.SingleAsync()).IsPlatformAdmin);
    }

    [Fact]
    public async Task A_setting_naming_an_account_that_does_not_exist_yet_does_nothing()
    {
        await using var db = NewDb();
        db.Users.Add(Account("someone.else@example.org"));
        await db.SaveChangesAsync();

        Assert.False(await For(db).EnsureAsync("boss@example.org"));

        Assert.False((await db.Users.SingleAsync()).IsPlatformAdmin);
    }

    [Fact]
    public async Task Once_any_operator_exists_the_setting_is_inert()
    {
        // Leaving Platform:BootstrapAdminEmail in place must never quietly
        // re-grant access that was deliberately revoked.
        await using var db = NewDb();
        db.Users.AddRange(Account("existing.operator@example.org", admin: true), Account("boss@example.org"));
        await db.SaveChangesAsync();

        Assert.False(await For(db).EnsureAsync("boss@example.org"));

        Assert.False((await db.Users.SingleAsync(u => u.Email == "boss@example.org")).IsPlatformAdmin);
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        await using var db = NewDb();
        db.Users.Add(Account("boss@example.org"));
        await db.SaveChangesAsync();
        var bootstrapper = For(db);

        Assert.True(await bootstrapper.EnsureAsync("boss@example.org"));
        Assert.False(await bootstrapper.EnsureAsync("boss@example.org"));
    }
}
