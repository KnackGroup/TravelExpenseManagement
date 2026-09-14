using Microsoft.EntityFrameworkCore;
using TravelExpense.Api.Data.Entities;

namespace TravelExpense.Api.Data;

/// <summary>
/// Database-first: the schema is created/versioned by db/schema.sql, not by EF migrations
/// (kept that way so the SQL is the single, reviewable source of truth for DBAs/finance).
/// This context just maps onto that schema for querying and writing.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    /// <summary>
    /// Auto-stamps CreatedAt/UpdatedAt-style columns (see ICreationTimestamped/
    /// IUpdateTimestamped in Data/Entities/Timestamps.cs) on every save, so no controller or
    /// service has to remember to set them by hand. Without this, EF Core includes every
    /// mapped scalar property in its INSERT statement, which means the column's SQL DEFAULT
    /// (SYSUTCDATETIME()) never actually fires - the CLR default (0001-01-01) would be
    /// written instead.
    /// </summary>
    private void StampTimestamps()
    {
        var utcNow = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added && entry.Entity is ICreationTimestamped created)
                created.StampCreated(utcNow);
            if (entry.State is EntityState.Added or EntityState.Modified && entry.Entity is IUpdateTimestamped updated)
                updated.StampUpdated(utcNow);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
    public DbSet<PolicyCap> PolicyCaps => Set<PolicyCap>();
    public DbSet<TourPlan> TourPlans => Set<TourPlan>();
    public DbSet<TourPlanStop> TourPlanStops => Set<TourPlanStop>();
    public DbSet<AdvanceRequest> AdvanceRequests => Set<AdvanceRequest>();
    public DbSet<AdvanceApproval> AdvanceApprovals => Set<AdvanceApproval>();
    public DbSet<AdvanceDisbursement> AdvanceDisbursements => Set<AdvanceDisbursement>();
    public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();
    public DbSet<ExpenseLineItem> ExpenseLineItems => Set<ExpenseLineItem>();
    public DbSet<DailyTourReport> DailyTourReports => Set<DailyTourReport>();
    public DbSet<TripFinancialSummaryRow> TripFinancialSummary => Set<TripFinancialSummaryRow>();
    public DbSet<PendingPolicyExceptionRow> PendingPolicyExceptions => Set<PendingPolicyExceptionRow>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<EmployeeNotificationPreference> EmployeeNotificationPreferences => Set<EmployeeNotificationPreference>();
    public DbSet<EmailOutboxEntry> EmailOutbox => Set<EmailOutboxEntry>();
    public DbSet<AiSettings> AiSettings => Set<AiSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>(e =>
        {
            e.ToTable("Channels");
            e.HasKey(x => x.ChannelId);
            e.HasIndex(x => x.ChannelCode).IsUnique();
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Roles");
            e.HasKey(x => x.RoleId);
            e.HasOne(x => x.Channel).WithMany(c => c.Roles).HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Employee>(e =>
        {
            e.ToTable("Employees");
            e.HasKey(x => x.EmployeeId);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.EmployeeCode).IsUnique();
            e.HasOne(x => x.Role).WithMany(r => r.Employees).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Manager).WithMany(m => m.DirectReports).HasForeignKey(x => x.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpenseCategory>(e =>
        {
            e.ToTable("ExpenseCategories");
            e.HasKey(x => x.ExpenseCategoryId);
            e.HasIndex(x => x.CategoryCode).IsUnique();
        });

        modelBuilder.Entity<PolicyCap>(e =>
        {
            e.ToTable("PolicyCaps");
            e.HasKey(x => x.PolicyCapId);
            e.Property(x => x.MaxAmountPerDay).HasColumnType("decimal(12,2)");
            e.Property(x => x.MaxAmountPerBooking).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.Role).WithMany(r => r.PolicyCaps).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ExpenseCategory).WithMany().HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TourPlan>(e =>
        {
            e.ToTable("TourPlans");
            e.HasKey(x => x.TourPlanId);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Channel).WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Stops).WithOne(s => s.TourPlan).HasForeignKey(s => s.TourPlanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TourPlanStop>(e =>
        {
            e.ToTable("TourPlanStops");
            e.HasKey(x => x.TourPlanStopId);
        });

        modelBuilder.Entity<AdvanceRequest>(e =>
        {
            e.ToTable("AdvanceRequests");
            e.HasKey(x => x.AdvanceRequestId);
            e.Property(x => x.RequestedAmount).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.TourPlan).WithMany(t => t.AdvanceRequests).HasForeignKey(x => x.TourPlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Disbursement).WithOne(d => d.AdvanceRequest).HasForeignKey<AdvanceDisbursement>(d => d.AdvanceRequestId);
        });

        modelBuilder.Entity<AdvanceApproval>(e =>
        {
            e.ToTable("AdvanceApprovals");
            e.HasKey(x => x.AdvanceApprovalId);
            e.Property(x => x.ApprovedAmount).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.AdvanceRequest).WithMany(r => r.Approvals).HasForeignKey(x => x.AdvanceRequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Approver).WithMany().HasForeignKey(x => x.ApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AdvanceDisbursement>(e =>
        {
            e.ToTable("AdvanceDisbursements");
            e.HasKey(x => x.AdvanceDisbursementId);
            e.Property(x => x.Amount).HasColumnType("decimal(12,2)");
            e.HasIndex(x => x.AdvanceRequestId).IsUnique();
            e.HasOne(x => x.DisbursedBy).WithMany().HasForeignKey(x => x.DisbursedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpenseReport>(e =>
        {
            e.ToTable("ExpenseReports");
            e.HasKey(x => x.ExpenseReportId);
            e.Property(x => x.TotalClaimedAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.TotalApprovedAmount).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.TourPlan).WithMany(t => t.ExpenseReports).HasForeignKey(x => x.TourPlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.LineItems).WithOne(l => l.ExpenseReport).HasForeignKey(l => l.ExpenseReportId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExpenseLineItem>(e =>
        {
            e.ToTable("ExpenseLineItems");
            e.HasKey(x => x.ExpenseLineItemId);
            e.Property(x => x.ClaimedAmount).HasColumnType("decimal(12,2)");
            e.Property(x => x.PolicyCapAmountApplied).HasColumnType("decimal(12,2)");
            e.Property(x => x.ApprovedAmount).HasColumnType("decimal(12,2)");
            e.HasOne(x => x.ExpenseCategory).WithMany().HasForeignKey(x => x.ExpenseCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Approver).WithMany().HasForeignKey(x => x.ApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DailyTourReport>(e =>
        {
            e.ToTable("DailyTourReports");
            e.HasKey(x => x.DailyTourReportId);
            e.HasIndex(x => new { x.TourPlanId, x.ReportDate }).IsUnique();
            e.HasOne(x => x.TourPlan).WithMany(t => t.DailyReports).HasForeignKey(x => x.TourPlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TripFinancialSummaryRow>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_TripFinancialSummary");
        });

        modelBuilder.Entity<PendingPolicyExceptionRow>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_PendingPolicyExceptions");
        });

        modelBuilder.Entity<EmailSettings>(e =>
        {
            e.ToTable("EmailSettings");
            e.HasKey(x => x.EmailSettingsId);
        });

        modelBuilder.Entity<EmailTemplate>(e =>
        {
            e.ToTable("EmailTemplates");
            e.HasKey(x => x.EmailTemplateId);
            e.HasIndex(x => x.EventType).IsUnique();
        });

        modelBuilder.Entity<EmployeeNotificationPreference>(e =>
        {
            e.ToTable("EmployeeNotificationPreferences");
            e.HasKey(x => x.EmployeeNotificationPreferenceId);
            e.HasIndex(x => new { x.EmployeeId, x.EventType }).IsUnique();
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailOutboxEntry>(e =>
        {
            e.ToTable("EmailOutbox");
            e.HasKey(x => x.EmailOutboxId);
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });

        modelBuilder.Entity<AiSettings>(e =>
        {
            e.ToTable("AiSettings");
            e.HasKey(x => x.AiSettingsId);
        });
    }
}
