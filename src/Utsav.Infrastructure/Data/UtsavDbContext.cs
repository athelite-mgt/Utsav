using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Utsav.Infrastructure.Data.Entities;

namespace Utsav.Infrastructure.Data;

public partial class UtsavDbContext : DbContext
{
    public UtsavDbContext(DbContextOptions<UtsavDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AdBooking> AdBookings { get; set; }

    public virtual DbSet<Advance> Advances { get; set; }

    public virtual DbSet<AdvanceSettlement> AdvanceSettlements { get; set; }

    public virtual DbSet<Advertiser> Advertisers { get; set; }

    public virtual DbSet<BudgetLine> BudgetLines { get; set; }

    public virtual DbSet<CashLedger> CashLedgers { get; set; }

    public virtual DbSet<Collector> Collectors { get; set; }

    public virtual DbSet<Contract> Contracts { get; set; }

    public virtual DbSet<ContractPayment> ContractPayments { get; set; }

    public virtual DbSet<VendorIssue> VendorIssues { get; set; }

    public virtual DbSet<Document> Documents { get; set; }

    public virtual DbSet<Donor> Donors { get; set; }

    public virtual DbSet<Expense> Expenses { get; set; }

    public virtual DbSet<Feast> Feasts { get; set; }

    public virtual DbSet<FeastExpense> FeastExpenses { get; set; }

    public virtual DbSet<FeastToken> FeastTokens { get; set; }

    public virtual DbSet<FundTransfer> FundTransfers { get; set; }

    public virtual DbSet<Lookup> Lookups { get; set; }

    public virtual DbSet<CollectionPayment> CollectionPayments { get; set; }

    public virtual DbSet<NotificationRecord> Notifications { get; set; }

    public virtual DbSet<PujaCalendarDay> PujaCalendarDays { get; set; }

    public virtual DbSet<Offering> Offerings { get; set; }

    public virtual DbSet<OrgMember> OrgMembers { get; set; }

    public virtual DbSet<OrgRoleMapping> OrgRoleMappings { get; set; }

    public virtual DbSet<Organization> Organizations { get; set; }

    public virtual DbSet<PujaSchedule> PujaSchedules { get; set; }

    public virtual DbSet<PujaYear> PujaYears { get; set; }

    public virtual DbSet<PushSubscriptionRecord> PushSubscriptionRecords { get; set; }

    public virtual DbSet<ApiKey> ApiKeys { get; set; }

    public virtual DbSet<Session> Sessions { get; set; }

    public virtual DbSet<SpecialDonation> SpecialDonations { get; set; }

    public virtual DbSet<Subscription> Subscriptions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    public virtual DbSet<VAdvanceBalance> VAdvanceBalances { get; set; }

    public virtual DbSet<VBudgetVsActual> VBudgetVsActuals { get; set; }

    public virtual DbSet<VContractStatus> VContractStatuses { get; set; }

    public virtual DbSet<Vendor> Vendors { get; set; }

    public virtual DbSet<WorkflowItem> WorkflowItems { get; set; }

    public virtual DbSet<WorkflowNote> WorkflowNotes { get; set; }

    public virtual DbSet<Zone> Zones { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdBooking>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("ad_bookings_pkey");

            entity.ToTable("ad_bookings");

            entity.HasIndex(e => e.YearId, "idx_ad_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.AdType).HasColumnName("ad_type");
            entity.Property(e => e.AdvertiserId).HasColumnName("advertiser_id");
            entity.Property(e => e.BillNo).HasColumnName("bill_no");
            entity.Property(e => e.Billed)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("billed");
            entity.Property(e => e.BilledOn).HasColumnName("billed_on");
            entity.Property(e => e.Collected)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("collected");
            entity.Property(e => e.CollectedOn).HasColumnName("collected_on");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Due)
                .HasPrecision(12, 2)
                .HasComputedColumnSql("(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true)
                .HasColumnName("due");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Projected)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("projected");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("updated_at");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Advertiser).WithMany(p => p.AdBookings)
                .HasForeignKey(d => d.AdvertiserId)
                .HasConstraintName("ad_bookings_advertiser_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.AdBookings)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("ad_bookings_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.AdBookings)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("ad_bookings_year_id_fkey");
        });

        modelBuilder.Entity<Advance>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("advances_pkey");

            entity.ToTable("advances");

            entity.HasIndex(e => e.YearId, "idx_advances_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.IssuedBy).HasColumnName("issued_by");
            entity.Property(e => e.IssuedOn).HasColumnName("issued_on");
            entity.Property(e => e.IssuedToCollectorId).HasColumnName("issued_to_collector_id");
            entity.Property(e => e.IssuedToName).HasColumnName("issued_to_name");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Purpose).HasColumnName("purpose");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'open'")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("updated_at");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.IssuedToCollector).WithMany(p => p.Advances)
                .HasForeignKey(d => d.IssuedToCollectorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("advances_issued_to_collector_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.Advances)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("advances_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.Advances)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("advances_year_id_fkey");
        });

        modelBuilder.Entity<AdvanceSettlement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("advance_settlements_pkey");

            entity.ToTable("advance_settlements");

            entity.HasIndex(e => e.AdvanceId, "idx_settlements_adv");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.AdvanceId).HasColumnName("advance_id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpenseId).HasColumnName("expense_id");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SettledOn).HasColumnName("settled_on");

            entity.HasOne(d => d.Advance).WithMany(p => p.AdvanceSettlements)
                .HasForeignKey(d => d.AdvanceId)
                .HasConstraintName("advance_settlements_advance_id_fkey");

            entity.HasOne(d => d.Expense).WithMany(p => p.AdvanceSettlements)
                .HasForeignKey(d => d.ExpenseId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("advance_settlements_expense_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.AdvanceSettlements)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("advance_settlements_org_id_fkey");
        });

        modelBuilder.Entity<Advertiser>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("advertisers_pkey");

            entity.ToTable("advertisers");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Contact).HasColumnName("contact");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");

            entity.HasOne(d => d.Org).WithMany(p => p.Advertisers)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("advertisers_org_id_fkey");
        });

        modelBuilder.Entity<BudgetLine>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("budget_lines_pkey");

            entity.ToTable("budget_lines");

            entity.HasIndex(e => e.YearId, "idx_budget_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ContractAmount)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("contract_amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Forecast)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("forecast");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.BudgetLines)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("budget_lines_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.BudgetLines)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("budget_lines_year_id_fkey");
        });

        modelBuilder.Entity<CashLedger>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("cash_ledger_pkey");

            entity.ToTable("cash_ledger");

            entity.HasIndex(e => e.YearId, "idx_cash_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.EntryDate).HasColumnName("entry_date");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Party).HasColumnName("party");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.CashLedgers)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("cash_ledger_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.CashLedgers)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("cash_ledger_year_id_fkey");
        });

        modelBuilder.Entity<Collector>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("collectors_pkey");

            entity.ToTable("collectors");

            entity.HasIndex(e => new { e.OrgId, e.Name }, "collectors_org_name_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Org).WithMany(p => p.Collectors)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("collectors_org_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Collectors)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("collectors_user_id_fkey");
        });

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("contracts_pkey");

            entity.ToTable("contracts");

            entity.HasIndex(e => e.YearId, "idx_contracts_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.AdvancePaid)
                .HasPrecision(12, 2)
                .HasColumnName("advance_paid");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ContractAmount)
                .HasPrecision(12, 2)
                .HasColumnName("contract_amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'draft'")
                .HasColumnName("status");
            entity.Property(e => e.Terms).HasColumnName("terms");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("updated_at");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("contracts_org_id_fkey");

            entity.HasOne(d => d.Vendor).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("contracts_vendor_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.Contracts)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("contracts_year_id_fkey");
        });

        modelBuilder.Entity<ContractPayment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("contract_payments_pkey");

            entity.ToTable("contract_payments");

            entity.HasIndex(e => e.ContractId, "idx_cpayments_ctr");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.ExpenseId).HasColumnName("expense_id");
            entity.Property(e => e.Milestone).HasColumnName("milestone");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.PaidDate).HasColumnName("paid_date");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pending'")
                .HasColumnName("status");

            entity.HasOne(d => d.Contract).WithMany(p => p.ContractPayments)
                .HasForeignKey(d => d.ContractId)
                .HasConstraintName("contract_payments_contract_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.ContractPayments)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("contract_payments_org_id_fkey");
        });

        modelBuilder.Entity<VendorIssue>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("vendor_issues_pkey");

            entity.ToTable("vendor_issues");

            entity.HasIndex(e => e.VendorId, "idx_vendor_issues_vendor");
            entity.HasIndex(e => e.ContractId, "idx_vendor_issues_contract");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");
            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.IssueType).HasColumnName("issue_type");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.OccurredOn).HasColumnName("occurred_on");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'open'")
                .HasColumnName("status");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");

            entity.HasOne(d => d.Org).WithMany()
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("vendor_issues_org_id_fkey");

            // Restrict, not Cascade: Vendor already reaches Organization
            // directly — same reasoning as contracts_vendor_id_fkey above,
            // SQL Server rejects more than one cascade path to the same table.
            entity.HasOne(d => d.Vendor).WithMany(p => p.VendorIssues)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("vendor_issues_vendor_id_fkey");

            // SetNull: an issue logged against a specific contract still
            // belongs to the vendor once that contract is deleted — same
            // pattern as documents_contract_id_fkey.
            entity.HasOne(d => d.Contract).WithMany(p => p.VendorIssues)
                .HasForeignKey(d => d.ContractId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("vendor_issues_contract_id_fkey");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("documents_pkey");

            entity.ToTable("documents");

            entity.HasIndex(e => e.YearId, "idx_documents_year");
            entity.HasIndex(e => e.ContractId, "idx_documents_contract");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.BlobName).HasColumnName("blob_name");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ContentType).HasColumnName("content_type");
            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.FileName).HasColumnName("file_name");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.UploadedByUserId).HasColumnName("uploaded_by_user_id");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany()
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("documents_org_id_fkey");

            // Restrict, not Cascade: Document already reaches Organization via
            // Org directly, via Year, and via Contract→Year — SQL Server
            // rejects more than one cascade path converging on the same
            // table. Year is the one to break: a document standing alone
            // once its year is gone just isn't expected to happen (delete
            // the documents first, same as any other year-scoped data).
            entity.HasOne(d => d.Year).WithMany()
                .HasForeignKey(d => d.YearId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("documents_year_id_fkey");

            entity.HasOne(d => d.Contract).WithMany()
                .HasForeignKey(d => d.ContractId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("documents_contract_id_fkey");

            entity.HasOne(d => d.UploadedBy).WithMany()
                .HasForeignKey(d => d.UploadedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("documents_uploaded_by_user_id_fkey");
        });

        modelBuilder.Entity<Donor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("donors_pkey");

            entity.ToTable("donors");

            entity.HasIndex(e => e.OrgId, "idx_donors_org");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Contact).HasColumnName("contact");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Surname).HasColumnName("surname");
            entity.Property(e => e.ZoneId).HasColumnName("zone_id");

            entity.HasOne(d => d.Org).WithMany(p => p.Donors)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("donors_org_id_fkey");

            entity.HasOne(d => d.Zone).WithMany(p => p.Donors)
                .HasForeignKey(d => d.ZoneId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("donors_zone_id_fkey");
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("expenses_pkey");

            entity.ToTable("expenses");

            entity.HasIndex(e => e.OrgId, "idx_expenses_org");

            entity.HasIndex(e => e.YearId, "idx_expenses_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.AdvanceId).HasColumnName("advance_id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BillNo).HasColumnName("bill_no");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.ContractId).HasColumnName("contract_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Details).HasColumnName("details");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SpentOn).HasColumnName("spent_on");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Contract).WithMany(p => p.Expenses)
                .HasForeignKey(d => d.ContractId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("expenses_contract_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.Expenses)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("expenses_org_id_fkey");

            entity.HasOne(d => d.Vendor).WithMany(p => p.Expenses)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("expenses_vendor_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.Expenses)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("expenses_year_id_fkey");
        });

        modelBuilder.Entity<FeastExpense>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("feast_expenses_pkey");

            entity.ToTable("feast_expenses");

            entity.HasIndex(e => e.OrgId, "idx_feast_expenses_org");

            entity.HasIndex(e => e.YearId, "idx_feast_expenses_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.BillNo).HasColumnName("bill_no");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Details).HasColumnName("details");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SpentOn).HasColumnName("spent_on");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.FeastExpenses)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("feast_expenses_org_id_fkey");

            entity.HasOne(d => d.Vendor).WithMany(p => p.FeastExpenses)
                .HasForeignKey(d => d.VendorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("feast_expenses_vendor_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.FeastExpenses)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("feast_expenses_year_id_fkey");
        });

        modelBuilder.Entity<FundTransfer>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("fund_transfers_pkey");

            entity.ToTable("fund_transfers");

            entity.HasIndex(e => e.YearId, "idx_transfers_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Direction)
                .HasDefaultValueSql("'cash_to_bank'")
                .HasColumnName("direction");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Reference).HasColumnName("reference");
            entity.Property(e => e.TransferDate).HasColumnName("transfer_date");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.FundTransfers)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fund_transfers_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.FundTransfers)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("fund_transfers_year_id_fkey");
        });

        modelBuilder.Entity<Lookup>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("lookups_pkey");

            entity.ToTable("lookups");

            entity.HasIndex(e => new { e.OrgId, e.Category, e.Label }, "lookups_org_category_label_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Label).HasColumnName("label");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");

            entity.HasOne(d => d.Org).WithMany(p => p.Lookups)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("lookups_org_id_fkey");
        });

        modelBuilder.Entity<CollectionPayment>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("collection_payments_pkey");

            entity.ToTable("collection_payments");

            entity.HasIndex(e => new { e.ParentType, e.ParentId }, "idx_collection_payments_parent");
            entity.HasIndex(e => new { e.YearId, e.OrgId }, "idx_collection_payments_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.PaidOn).HasColumnName("paid_on");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.ParentType).HasColumnName("parent_type");
            entity.Property(e => e.RecordedByName).HasColumnName("recorded_by_name");
            entity.Property(e => e.RecordedByUserId).HasColumnName("recorded_by_user_id");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany()
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("collection_payments_org_id_fkey");

            // Restrict, not cascade: Org already cascades to this table, and
            // SQL Server rejects two cascade paths to the same row. Payments
            // are cleared explicitly by the save endpoint before a year is ever
            // removed, so this never actually blocks a real delete.
            entity.HasOne(d => d.Year).WithMany()
                .HasForeignKey(d => d.YearId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("collection_payments_year_id_fkey");
        });

        modelBuilder.Entity<PujaCalendarDay>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("puja_calendar_days_pkey");

            entity.ToTable("puja_calendar_days");

            entity.HasIndex(e => new { e.YearId, e.OrgId }, "idx_puja_calendar_days_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.EventDate).HasColumnName("event_date");
            entity.Property(e => e.Label).HasColumnName("label");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany()
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("puja_calendar_days_org_id_fkey");

            // Restrict, not cascade: Org already cascades here, and SQL Server
            // rejects two cascade paths to the same row.
            entity.HasOne(d => d.Year).WithMany()
                .HasForeignKey(d => d.YearId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("puja_calendar_days_year_id_fkey");
        });

        modelBuilder.Entity<NotificationRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notifications_pkey");

            entity.ToTable("notifications");

            entity.HasIndex(e => new { e.OrgId, e.CreatedAt }, "idx_notifications_org_created");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.ActorName).HasColumnName("actor_name");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.Body).HasColumnName("body");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Kind).HasColumnName("kind");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.Url).HasColumnName("url");

            entity.HasOne(d => d.Org).WithMany()
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("notifications_org_id_fkey");
        });

        modelBuilder.Entity<Offering>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("offerings_pkey");

            entity.ToTable("offerings");

            entity.HasIndex(e => e.YearId, "idx_offerings_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Contact).HasColumnName("contact");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Details).HasColumnName("details");
            entity.Property(e => e.ExpectedDate).HasColumnName("expected_date");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OfferingType).HasColumnName("offering_type");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.PujaDay).HasColumnName("puja_day");
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.ReceivedBy).HasColumnName("received_by");
            entity.Property(e => e.ReceivedDate).HasColumnName("received_date");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'pledged'")
                .HasColumnName("status");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.Offerings)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("offerings_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.Offerings)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("offerings_year_id_fkey");
        });

        modelBuilder.Entity<OrgMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("org_members_pkey");

            entity.ToTable("org_members");

            entity.HasIndex(e => e.UserId, "idx_orgmembers_user");

            entity.HasIndex(e => new { e.OrgId, e.UserId }, "org_members_org_id_user_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Role)
                .HasDefaultValueSql("'viewer'")
                .HasColumnName("role");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'active'")
                .HasColumnName("status");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Org).WithMany(p => p.OrgMembers)
                .HasForeignKey(d => d.OrgId)
                .HasConstraintName("org_members_org_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.OrgMembers)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("org_members_user_id_fkey");
        });

        modelBuilder.Entity<OrgRoleMapping>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("org_role_mappings_pkey");

            entity.ToTable("org_role_mappings");

            entity.HasIndex(e => new { e.OrgId, e.EntraGroupId }, "org_role_mappings_org_id_entra_group_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.EntraGroupId).HasColumnName("entra_group_id");
            entity.Property(e => e.EntraGroupName).HasColumnName("entra_group_name");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Role).HasColumnName("role");

            entity.HasOne(d => d.Org).WithMany(p => p.OrgRoleMappings)
                .HasForeignKey(d => d.OrgId)
                .HasConstraintName("org_role_mappings_org_id_fkey");
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("organizations_pkey");

            entity.ToTable("organizations");

            entity.HasIndex(e => e.Slug, "organizations_slug_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.EntraTenantId).HasColumnName("entra_tenant_id");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Slug).HasColumnName("slug");
        });

        modelBuilder.Entity<PujaSchedule>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("puja_schedule_pkey");

            entity.ToTable("puja_schedule");

            entity.HasIndex(e => e.YearId, "idx_schedule_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.AssigneeCollectorId).HasColumnName("assignee_collector_id");
            entity.Property(e => e.AssigneeName).HasColumnName("assignee_name");
            entity.Property(e => e.ChargeTitle).HasColumnName("charge_title");
            entity.Property(e => e.Contact).HasColumnName("contact");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.EndTime).HasColumnName("end_time");
            entity.Property(e => e.EventDate).HasColumnName("event_date");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.PujaDay).HasColumnName("puja_day");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.StartTime).HasColumnName("start_time");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.AssigneeCollector).WithMany(p => p.PujaSchedules)
                .HasForeignKey(d => d.AssigneeCollectorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("puja_schedule_assignee_collector_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.PujaSchedules)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("puja_schedule_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.PujaSchedules)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("puja_schedule_year_id_fkey");
        });

        modelBuilder.Entity<PujaYear>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("puja_years_pkey");

            entity.ToTable("puja_years");

            entity.HasIndex(e => new { e.OrgId, e.Year }, "puja_years_org_year_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OpeningCashBalance)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("opening_cash_balance");
            entity.Property(e => e.OpeningBankBalance)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("opening_bank_balance");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.ProjectionPct)
                .HasPrecision(5, 2)
                .HasDefaultValue(6.50m)
                .HasColumnName("projection_pct");
            entity.Property(e => e.Target)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("target");
            entity.Property(e => e.Year).HasColumnName("year");

            entity.HasOne(d => d.Org).WithMany(p => p.PujaYears)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("puja_years_org_id_fkey");
        });

        modelBuilder.Entity<ApiKey>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("api_keys_pkey");

            entity.ToTable("api_keys");

            // Every authenticated agent request looks a key up by its hash,
            // so that lookup must be an index seek, and two keys can never
            // share one hash.
            entity.HasIndex(e => e.KeyHash, "api_keys_key_hash_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.KeyHash)
                .HasMaxLength(64)
                .HasColumnName("key_hash");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.Property(e => e.Name)
                .HasMaxLength(128)
                .HasColumnName("name");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Prefix)
                .HasMaxLength(24)
                .HasColumnName("prefix");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");

            entity.HasOne(d => d.Org).WithMany(p => p.ApiKeys)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("api_keys_org_id_fkey");

            // The issuer's account going away must not take the key (or the
            // agent depending on it) with it — the key is the committee's,
            // not that member's.
            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.ApiKeys)
                .HasForeignKey(d => d.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("api_keys_created_by_user_id_fkey");
        });

        modelBuilder.Entity<PushSubscriptionRecord>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("push_subscriptions_pkey");

            entity.ToTable("push_subscriptions");

            // The endpoint URL is what the push service itself uses to
            // address the device, so it's already unique per browser
            // installation — re-subscribing the same device (e.g. after
            // signing in as someone else) replaces the row instead of piling
            // up duplicates that would each get their own copy of a push.
            entity.HasIndex(e => e.Endpoint, "push_subscriptions_endpoint_key").IsUnique();
            entity.HasIndex(e => e.UserId, "idx_push_subscriptions_user");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Auth).HasColumnName("auth");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Endpoint)
                .HasMaxLength(1024)
                .HasColumnName("endpoint");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.P256dh).HasColumnName("p256dh");
            entity.Property(e => e.UserAgent)
                .HasMaxLength(512)
                .HasColumnName("user_agent");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Org).WithMany(p => p.PushSubscriptionRecords)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("push_subscriptions_org_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.PushSubscriptionRecords)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("push_subscriptions_user_id_fkey");
        });

        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Token).HasName("sessions_pkey");

            entity.ToTable("sessions");

            entity.HasIndex(e => e.UserId, "idx_sessions_user");

            entity.Property(e => e.Token).HasColumnName("token");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Org).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("sessions_org_id_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.Sessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("sessions_user_id_fkey");
        });

        modelBuilder.Entity<SpecialDonation>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("special_donations_pkey");

            entity.ToTable("special_donations");

            entity.HasIndex(e => e.YearId, "idx_special_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.BillNo).HasColumnName("bill_no");
            entity.Property(e => e.Billed)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("billed");
            entity.Property(e => e.Collected)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("collected");
            entity.Property(e => e.CollectedOn).HasColumnName("collected_on");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.Contact).HasColumnName("contact");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Due)
                .HasPrecision(12, 2)
                .HasComputedColumnSql("(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true)
                .HasColumnName("due");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Projected)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("projected");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("updated_at");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.SpecialDonations)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("special_donations_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.SpecialDonations)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("special_donations_year_id_fkey");
        });

        modelBuilder.Entity<Feast>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("feasts_pkey");

            entity.ToTable("feasts");

            entity.HasIndex(e => e.OrgId, "idx_feast_org");

            entity.HasIndex(e => e.YearId, "idx_feast_year");

            entity.HasIndex(e => new { e.DonorId, e.YearId }, "feasts_donor_id_year_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.BillNo).HasColumnName("bill_no");
            entity.Property(e => e.Billed)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("billed");
            entity.Property(e => e.BilledOn).HasColumnName("billed_on");
            entity.Property(e => e.Collected)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("collected");
            entity.Property(e => e.CollectedOn).HasColumnName("collected_on");
            entity.Property(e => e.CollectorId).HasColumnName("collector_id");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.DonorId).HasColumnName("donor_id");
            entity.Property(e => e.Due)
                .HasPrecision(12, 2)
                .HasComputedColumnSql("(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true)
                .HasColumnName("due");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.NotEligible)
                .HasDefaultValue(false)
                .HasColumnName("not_eligible");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("updated_at");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Collector).WithMany(p => p.Feasts)
                .HasForeignKey(d => d.CollectorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("feasts_collector_id_fkey");

            entity.HasOne(d => d.Donor).WithMany(p => p.Feasts)
                .HasForeignKey(d => d.DonorId)
                .HasConstraintName("feasts_donor_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.Feasts)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("feasts_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.Feasts)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("feasts_year_id_fkey");
        });

        modelBuilder.Entity<FeastToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("feast_tokens_pkey");

            entity.ToTable("feast_tokens");

            entity.HasIndex(e => e.Code, "feast_tokens_code_key").IsUnique();

            entity.HasIndex(e => e.OrgId, "idx_feast_tokens_org");

            entity.HasIndex(e => e.YearId, "idx_feast_tokens_year");

            entity.HasIndex(e => e.FeastId, "idx_feast_tokens_feast");

            entity.HasIndex(e => e.Status, "idx_feast_tokens_status");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.YearId).HasColumnName("year_id");
            entity.Property(e => e.SeqNo).HasColumnName("seq_no");
            entity.Property(e => e.Code)
                .HasMaxLength(32)
                .HasColumnName("code");
            entity.Property(e => e.FeastId).HasColumnName("feast_id");
            entity.Property(e => e.Status)
                .HasMaxLength(16)
                .HasDefaultValue(FeastTokenStatus.Unassigned)
                .HasColumnName("status");
            entity.Property(e => e.AssignedAt).HasColumnName("assigned_at");
            entity.Property(e => e.RedeemedAt).HasColumnName("redeemed_at");
            entity.Property(e => e.RedeemedByUserId).HasColumnName("redeemed_by_user_id");
            entity.Property(e => e.RedeemedByName).HasColumnName("redeemed_by_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");

            entity.HasOne(d => d.FeastRecord).WithMany(p => p.Tokens)
                .HasForeignKey(d => d.FeastId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("feast_tokens_feast_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.FeastTokens)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("feast_tokens_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.FeastTokens)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("feast_tokens_year_id_fkey");

            entity.HasOne<User>().WithMany()
                .HasForeignKey(d => d.RedeemedByUserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("feast_tokens_redeemed_by_user_id_fkey");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("subscriptions_pkey");

            entity.ToTable("subscriptions");

            entity.HasIndex(e => e.OrgId, "idx_sub_org");

            entity.HasIndex(e => e.YearId, "idx_sub_year");

            entity.HasIndex(e => new { e.DonorId, e.YearId }, "subscriptions_donor_id_year_id_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.BillNo).HasColumnName("bill_no");
            entity.Property(e => e.Billed)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("billed");
            entity.Property(e => e.BilledOn).HasColumnName("billed_on");
            entity.Property(e => e.Collected)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("collected");
            entity.Property(e => e.CollectedOn).HasColumnName("collected_on");
            entity.Property(e => e.CollectorId).HasColumnName("collector_id");
            entity.Property(e => e.Comments).HasColumnName("comments");
            entity.Property(e => e.Category)
                .HasMaxLength(32)
                .HasDefaultValue(SubscriptionCategory.Membership)
                .HasColumnName("category");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.DonorId).HasColumnName("donor_id");
            entity.Property(e => e.Due)
                .HasPrecision(12, 2)
                .HasComputedColumnSql("(COALESCE([billed], 0) - COALESCE([collected], 0))", stored: true)
                .HasColumnName("due");
            entity.Property(e => e.History)
                .HasColumnType("nvarchar(max)")
                .HasColumnName("history");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.NotEligible)
                .HasDefaultValue(false)
                .HasColumnName("not_eligible");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.PriorBilled)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("prior_billed");
            entity.Property(e => e.Projected)
                .HasPrecision(12, 2)
                .HasDefaultValue(0m)
                .HasColumnName("projected");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("updated_at");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Collector).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.CollectorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("subscriptions_collector_id_fkey");

            entity.HasOne(d => d.Donor).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.DonorId)
                .HasConstraintName("subscriptions_donor_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("subscriptions_org_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.Subscriptions)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("subscriptions_year_id_fkey");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users");

            entity.HasIndex(e => e.Email, "users_email_key").IsUnique();

            entity.HasIndex(e => e.EntraObjectId, "users_entra_object_id_key").IsUnique();

            entity.HasIndex(e => e.GoogleSub, "users_google_sub_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.AuthProvider)
                .HasDefaultValueSql("'local'")
                .HasColumnName("auth_provider");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.EntraObjectId).HasColumnName("entra_object_id");
            entity.Property(e => e.FullName).HasColumnName("full_name");
            entity.Property(e => e.GoogleSub).HasColumnName("google_sub");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.Role)
                .HasDefaultValueSql("'collector'")
                .HasColumnName("role");
        });

        modelBuilder.Entity<VAdvanceBalance>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_advance_balances");

            entity.Property(e => e.Amount)
                .HasPrecision(12, 2)
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IssuedBy).HasColumnName("issued_by");
            entity.Property(e => e.IssuedOn).HasColumnName("issued_on");
            entity.Property(e => e.IssuedToCollectorId).HasColumnName("issued_to_collector_id");
            entity.Property(e => e.IssuedToName).HasColumnName("issued_to_name");
            entity.Property(e => e.Mode).HasColumnName("mode");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Outstanding).HasColumnName("outstanding");
            entity.Property(e => e.Purpose).HasColumnName("purpose");
            entity.Property(e => e.Returned).HasColumnName("returned");
            entity.Property(e => e.Settled).HasColumnName("settled");
            entity.Property(e => e.Spent).HasColumnName("spent");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.YearId).HasColumnName("year_id");
        });

        modelBuilder.Entity<VBudgetVsActual>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_budget_vs_actual");

            entity.Property(e => e.Actual).HasColumnName("actual");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ContractAmount)
                .HasPrecision(12, 2)
                .HasColumnName("contract_amount");
            entity.Property(e => e.Contracted)
                .HasPrecision(12, 2)
                .HasColumnName("contracted");
            entity.Property(e => e.Forecast)
                .HasPrecision(12, 2)
                .HasColumnName("forecast");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.PriorActual).HasColumnName("prior_actual");
            entity.Property(e => e.Remaining).HasColumnName("remaining");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.YearId).HasColumnName("year_id");
        });

        modelBuilder.Entity<VContractStatus>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("v_contract_status");

            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.ContractAmount)
                .HasPrecision(12, 2)
                .HasColumnName("contract_amount");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.EndDate).HasColumnName("end_date");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.Outstanding).HasColumnName("outstanding");
            entity.Property(e => e.Paid).HasColumnName("paid");
            entity.Property(e => e.Scheduled).HasColumnName("scheduled");
            entity.Property(e => e.StartDate).HasColumnName("start_date");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.Terms).HasColumnName("terms");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.VendorId).HasColumnName("vendor_id");
            entity.Property(e => e.VendorName).HasColumnName("vendor_name");
            entity.Property(e => e.YearId).HasColumnName("year_id");
        });

        modelBuilder.Entity<Vendor>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("vendors_pkey");

            entity.ToTable("vendors");

            entity.HasIndex(e => e.OrgId, "idx_vendors_org");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Active)
                .HasDefaultValue(true)
                .HasColumnName("active");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Contact).HasColumnName("contact");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.GstNo).HasColumnName("gst_no");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");

            entity.HasOne(d => d.Org).WithMany(p => p.Vendors)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("vendors_org_id_fkey");
        });

        modelBuilder.Entity<WorkflowItem>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workflow_items_pkey");

            entity.ToTable("workflow_items");

            entity.HasIndex(e => e.YearId, "idx_workflow_year");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.CompletedDate).HasColumnName("completed_date");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.DueDate).HasColumnName("due_date");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.OwnerCollectorId).HasColumnName("owner_collector_id");
            entity.Property(e => e.OwnerName).HasColumnName("owner_name");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'not_started'")
                .HasColumnName("status");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("updated_at");
            entity.Property(e => e.YearId).HasColumnName("year_id");

            entity.HasOne(d => d.Org).WithMany(p => p.WorkflowItems)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("workflow_items_org_id_fkey");

            entity.HasOne(d => d.OwnerCollector).WithMany(p => p.WorkflowItems)
                .HasForeignKey(d => d.OwnerCollectorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("workflow_items_owner_collector_id_fkey");

            entity.HasOne(d => d.Year).WithMany(p => p.WorkflowItems)
                .HasForeignKey(d => d.YearId)
                .HasConstraintName("workflow_items_year_id_fkey");
        });

        modelBuilder.Entity<WorkflowNote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("workflow_notes_pkey");

            entity.ToTable("workflow_notes");

            entity.HasIndex(e => e.WorkflowItemId, "idx_worknotes_item");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.AuthorId).HasColumnName("author_id");
            entity.Property(e => e.AuthorName).HasColumnName("author_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("SYSUTCDATETIME()")
                .HasColumnName("created_at");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.WorkflowItemId).HasColumnName("workflow_item_id");

            entity.HasOne(d => d.Author).WithMany(p => p.WorkflowNotes)
                .HasForeignKey(d => d.AuthorId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("workflow_notes_author_id_fkey");

            entity.HasOne(d => d.Org).WithMany(p => p.WorkflowNotes)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("workflow_notes_org_id_fkey");

            entity.HasOne(d => d.WorkflowItem).WithMany(p => p.WorkflowNotes)
                .HasForeignKey(d => d.WorkflowItemId)
                .HasConstraintName("workflow_notes_workflow_item_id_fkey");
        });

        modelBuilder.Entity<Zone>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("zones_pkey");

            entity.ToTable("zones");

            entity.HasIndex(e => new { e.OrgId, e.Name }, "zones_org_name_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("NEWSEQUENTIALID()")
                .HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.OrgId).HasColumnName("org_id");
            entity.Property(e => e.SortOrder)
                .HasDefaultValue(0)
                .HasColumnName("sort_order");

            entity.HasOne(d => d.Org).WithMany(p => p.Zones)
                .HasForeignKey(d => d.OrgId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("zones_org_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
