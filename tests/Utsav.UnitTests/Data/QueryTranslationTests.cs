using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.UnitTests.Data;

/// <summary>
/// Guards the class of bug that broke Google sign-in: a predicate handed to EF
/// as a compiled <see cref="Func{T, TResult}"/> instead of an
/// <see cref="Expression"/>. It compiles, and it looks right, but the provider
/// cannot turn an invoked delegate into a WHERE clause and throws
/// "could not be translated" — at runtime, on the sign-in path, where it took
/// a real user hitting a 500 to notice.
///
/// EF builds SQL without contacting the server, so these run against a
/// connection string that goes nowhere. Nothing here needs Azure SQL to exist.
/// </summary>
public class QueryTranslationTests
{
    private static UtsavDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<UtsavDbContext>()
            .UseSqlServer("Server=translation-only;Database=none;")
            .Options;

        return new UtsavDbContext(options);
    }

    /// <summary>
    /// The exact shapes <c>ExternalIdentityService.ResolveAsync</c> is called
    /// with, one per sign-in provider.
    /// </summary>
    public static TheoryData<string, Expression<Func<User, bool>>> ProviderLookups() => new()
    {
        { "google", u => u.GoogleSub == "sub-123" },
        { "entra", u => u.EntraObjectId == "oid-123" },
        { "email", u => u.Email == "someone@example.com" },
    };

    [Theory]
    [MemberData(nameof(ProviderLookups))]
    public void Provider_lookups_translate_to_sql(string provider, Expression<Func<User, bool>> predicate)
    {
        using var db = NewContext();

        var sql = db.Users.Where(predicate).ToQueryString();

        Assert.Contains("SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(provider));
    }

    /// <summary>
    /// The failure itself, pinned. If a future refactor takes a
    /// <c>Func&lt;User, bool&gt;</c> again, this is what it looks like — and
    /// this test documents that EF rejects it rather than silently pulling the
    /// whole users table into memory to filter it there.
    /// </summary>
    [Fact]
    public void A_compiled_delegate_predicate_is_rejected_rather_than_evaluated_in_memory()
    {
        using var db = NewContext();
        Func<User, bool> compiled = u => u.GoogleSub == "sub-123";

        Assert.Throws<InvalidOperationException>(() => db.Users.Where(u => compiled(u)).ToQueryString());
    }

    /// <summary>
    /// The advance/settlement views are read through raw SQL, but the entity
    /// side of the same tables still has to translate — a broken mapping here
    /// would surface as a 500 on the Advances page rather than at build time.
    /// </summary>
    [Fact]
    public void Org_and_year_scoped_reads_translate_to_sql()
    {
        using var db = NewContext();
        var org = Guid.NewGuid();
        var year = Guid.NewGuid();

        Assert.Contains("SELECT", db.Subscriptions.Where(s => s.OrgId == org && s.YearId == year).ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT", db.Expenses.Where(e => e.OrgId == org && e.YearId == year).ToQueryString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SELECT", db.OrgMembers.Where(m => m.OrgId == org && m.Status == "active").ToQueryString(), StringComparison.OrdinalIgnoreCase);
    }
}
