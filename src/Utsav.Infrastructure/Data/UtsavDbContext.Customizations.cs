using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Data;

/// <summary>
/// SQL Server specifics layered on top of the generated model configuration.
///
/// The original schema was Postgres, where <c>TEXT</c> is unbounded and freely
/// indexable. SQL Server is stricter: an unbounded <c>nvarchar(max)</c> is
/// stored off-row and cannot participate in a key or index at all, so every
/// column that backs a primary key or unique index needs a real length. Rather
/// than annotate ~200 properties one by one, strings default to a sane bounded
/// length and only the genuinely free-text columns are widened back to
/// <c>nvarchar(max)</c>.
/// </summary>
public partial class UtsavDbContext
{
    /// <summary>
    /// Long-form user prose. These are never indexed, never compared, and a
    /// committee member should not have an expense note truncated because it
    /// ran past a limit invented here.
    /// </summary>
    private static readonly (Type Entity, string Property)[] FreeTextColumns =
    [
        (typeof(PujaYear), nameof(PujaYear.Notes)),
        (typeof(Donor), nameof(Donor.Notes)),
        (typeof(Advertiser), nameof(Advertiser.Notes)),
        (typeof(Subscription), nameof(Subscription.Comments)),
        // Rollover stashes prior-year figures here as a JSON document.
        (typeof(Subscription), nameof(Subscription.History)),
        (typeof(AdBooking), nameof(AdBooking.Comments)),
        (typeof(SpecialDonation), nameof(SpecialDonation.Comments)),
        (typeof(Feast), nameof(Feast.Comments)),
        (typeof(Offering), nameof(Offering.Details)),
        (typeof(Expense), nameof(Expense.Details)),
        (typeof(Expense), nameof(Expense.Comment)),
        (typeof(FeastExpense), nameof(FeastExpense.Details)),
        (typeof(FeastExpense), nameof(FeastExpense.Comment)),
        (typeof(BudgetLine), nameof(BudgetLine.Notes)),
        (typeof(CashLedger), nameof(CashLedger.Note)),
        (typeof(FundTransfer), nameof(FundTransfer.Note)),
        (typeof(Vendor), nameof(Vendor.Notes)),
        (typeof(Vendor), nameof(Vendor.Address)),
        (typeof(Contract), nameof(Contract.Terms)),
        (typeof(Contract), nameof(Contract.Notes)),
        (typeof(ContractPayment), nameof(ContractPayment.Note)),
        (typeof(Advance), nameof(Advance.Purpose)),
        (typeof(Advance), nameof(Advance.Notes)),
        (typeof(AdvanceSettlement), nameof(AdvanceSettlement.Note)),
        (typeof(PujaSchedule), nameof(PujaSchedule.Notes)),
        (typeof(WorkflowItem), nameof(WorkflowItem.Notes)),
        (typeof(WorkflowNote), nameof(WorkflowNote.Note)),
    ];

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Bounded by default so string columns stay in-row and indexable;
        // the exceptions are widened again below.
        configurationBuilder.Properties<string>().HaveMaxLength(256);
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
    }

    public virtual DbSet<OrgInvite> OrgInvites { get; set; }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrgInvite>(entity =>
        {
            entity.ToTable("org_invites");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CodeHash, "org_invites_code_hash_key").IsUnique();
            entity.HasIndex(e => e.OrgId, "IX_org_invites_org_id");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(32);
            entity.Property(e => e.CodeHash).HasColumnName("code_hash").HasMaxLength(64);
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RedeemedAt).HasColumnName("redeemed_at");
            entity.Property(e => e.RedeemedByUserId).HasColumnName("redeemed_by_user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");

            entity.HasOne(e => e.Org).WithMany().HasForeignKey(e => e.OrgId);
        });

        foreach (var (entity, property) in FreeTextColumns)
        {
            modelBuilder.Entity(entity).Property(property).HasColumnType("nvarchar(max)");
        }

        modelBuilder.Entity<User>().Property(u => u.IsPlatformAdmin)
            .HasColumnName("is_platform_admin")
            .HasDefaultValue(false);

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.Property(e => e.LogoUrl).HasColumnName("logo_url").HasMaxLength(1024);
            entity.Property(e => e.AccentColour).HasColumnName("accent_colour").HasMaxLength(7);
        });

        RelaxOrgCascades(modelBuilder);
        BreakSecondaryCascadePaths(modelBuilder);

        // Last, so it layers on top of the generated configuration.
        ApplyTenantFilters(modelBuilder);

        // Backs the sessions primary key — 32 random bytes rendered as hex.
        modelBuilder.Entity<Session>().Property(s => s.Token).HasMaxLength(64);

        // Unique-indexed, and the RFC 5321 maximum is longer than the default.
        modelBuilder.Entity<User>().Property(u => u.Email).HasMaxLength(320);

        AddCompositeScopeIndexes(modelBuilder);
    }

    /// <summary>
    /// The one deliberate schema divergence from Postgres.
    ///
    /// Postgres declares <c>ON DELETE CASCADE</c> on both <c>org_id</c> and the
    /// natural parent (<c>year_id</c>, <c>donor_id</c>, <c>contract_id</c>, …).
    /// SQL Server refuses that outright: every year-scoped table is reachable
    /// from <c>organizations</c> twice — directly, and again through
    /// <c>puja_years</c> — which is error 1785, "may cause cycles or multiple
    /// cascade paths". One of the two paths has to give.
    ///
    /// Breaking the <c>org_id</c> path is the cheap one. The API never deletes
    /// an organization — <c>organizations</c> is absent from the writable
    /// <c>TABLES</c> whitelist in <c>app.mjs</c> and the only org route is
    /// <c>PATCH /api/organizations/current</c>, a rename. So nothing the
    /// application actually does changes, while every cascade it *does* rely on
    /// is preserved exactly: deleting a donor still removes their
    /// subscriptions, deleting a collector still nulls the rows pointing at
    /// them, deleting a puja year still takes its children with it.
    ///
    /// The cost is confined to tenant offboarding: dropping an organization row
    /// by hand now needs its children removed first, rather than happening in
    /// one statement.
    /// </summary>
    private static void RelaxOrgCascades(ModelBuilder modelBuilder)
    {
        var orgForeignKeys = modelBuilder.Model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetForeignKeys())
            .Where(fk => fk.Properties.Count == 1
                      && fk.Properties[0].Name == nameof(Zone.OrgId));

        foreach (var foreignKey in orgForeignKeys)
        {
            foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }

    /// <summary>
    /// Two more edges SQL Server won't accept, both between tables that already
    /// cascade from <c>puja_years</c>:
    ///
    ///   • <c>expenses.contract_id</c> — puja_years reaches expenses directly
    ///     and again through contracts.
    ///   • <c>advance_settlements.expense_id</c> — reached through advances and
    ///     again through expenses.
    ///
    /// Both are <c>ON DELETE SET NULL</c> in Postgres, and that behaviour is
    /// load-bearing: deleting a contract is supposed to leave its expenses
    /// intact and merely untag them ("nothing is lost", as app.mjs puts it).
    /// Demoting them to NO ACTION at the database level would turn those
    /// deletes into foreign-key errors, so the null-out is done explicitly in
    /// the delete endpoints instead — same outcome, just enforced a layer up.
    /// See ContractEndpoints and the generic CRUD delete handler.
    /// </summary>
    private static void BreakSecondaryCascadePaths(ModelBuilder modelBuilder)
    {
        SetNoAction<Expense>(modelBuilder, nameof(Expense.ContractId));
        SetNoAction<AdvanceSettlement>(modelBuilder, nameof(AdvanceSettlement.ExpenseId));
        // Same shape again: puja_years cascades to feast_tokens directly
        // (year_id) and a second time via feasts (puja_years -> feasts ->
        // feast_tokens.feast_id). The null-out on feast delete is done
        // explicitly instead — see TableRegistry.DetachBeforeDelete.
        SetNoAction<FeastToken>(modelBuilder, nameof(FeastToken.FeastId));
    }

    private static void SetNoAction<TEntity>(ModelBuilder modelBuilder, string foreignKeyProperty)
        where TEntity : class
    {
        var foreignKey = modelBuilder.Entity<TEntity>().Metadata
            .GetForeignKeys()
            .Single(fk => fk.Properties.Count == 1 && fk.Properties[0].Name == foreignKeyProperty);

        foreignKey.DeleteBehavior = DeleteBehavior.NoAction;
    }

    private static void AddCompositeScopeIndexes(ModelBuilder modelBuilder)
    {
        // The query pattern behind essentially every screen: rows for one
        // committee, in one puja year. Postgres only ever indexed these
        // columns separately, so the composite is new here (TASKLIST §7.8).
        modelBuilder.Entity<Subscription>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<Feast>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<FeastToken>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<AdBooking>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<SpecialDonation>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<Offering>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<BudgetLine>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<CashLedger>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<FundTransfer>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<PujaSchedule>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<WorkflowItem>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<Contract>().HasIndex(e => new { e.OrgId, e.YearId });
        modelBuilder.Entity<Expense>().HasIndex(e => new { e.OrgId, e.YearId, e.Category });
        modelBuilder.Entity<FeastExpense>().HasIndex(e => new { e.OrgId, e.YearId, e.Category });
        modelBuilder.Entity<Advance>().HasIndex(e => new { e.OrgId, e.YearId, e.Status });
    }
}
