using Microsoft.EntityFrameworkCore;
using FinSight.Models;

namespace FinSight.Data
{
    public class FinSightDbContext : DbContext
    {
        public FinSightDbContext(DbContextOptions<FinSightDbContext> options)
            : base(options)
        {
        }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<Budget> Budgets { get; set; }
        public DbSet<Forecast> Forecasts { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<Scenario> Scenarios { get; set; }
        public DbSet<ScenarioDetail> ScenarioDetails { get; set; }
        public DbSet<BudgetRequest> BudgetRequests { get; set; }
        public DbSet<Plan> Plans { get; set; }
        public DbSet<Subscription> Subscriptions { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<UserOTP> UserOTPs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Email should be unique
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Scenario → ScenarioDetails (cascade delete)
            modelBuilder.Entity<ScenarioDetail>()
                .HasOne(sd => sd.Scenario)
                .WithMany(s => s.ScenarioDetails)
                .HasForeignKey(sd => sd.ScenarioID)
                .OnDelete(DeleteBehavior.Cascade);

            // ScenarioDetail → Budget (no cascade to avoid multiple cascade paths)
            modelBuilder.Entity<ScenarioDetail>()
                .HasOne(sd => sd.Budget)
                .WithMany()
                .HasForeignKey(sd => sd.BudgetID)
                .OnDelete(DeleteBehavior.Restrict);

            // ScenarioDetail → Department (no cascade)
            modelBuilder.Entity<ScenarioDetail>()
                .HasOne(sd => sd.Department)
                .WithMany()
                .HasForeignKey(sd => sd.DepartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // Tenant → Users (one-to-many)
            modelBuilder.Entity<Tenant>()
                .HasMany(t => t.Users)
                .WithOne(u => u.Tenant)
                .HasForeignKey(u => u.TenantID)
                .OnDelete(DeleteBehavior.Restrict);

            // User → Department (no cascade to avoid multiple cascade paths)
            modelBuilder.Entity<User>()
                .HasOne(u => u.Department)
                .WithMany()
                .HasForeignKey(u => u.DepartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense → Budget (no cascade to avoid multiple cascade paths)
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Budget)
                .WithMany()
                .HasForeignKey(e => e.BudgetID)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense → Department (no cascade)
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Department)
                .WithMany()
                .HasForeignKey(e => e.DepartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // Expense → BudgetRequest (no cascade)
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.BudgetRequest)
                .WithMany()
                .HasForeignKey(e => e.BudgetRequestID)
                .OnDelete(DeleteBehavior.Restrict);

            // ── BudgetRequest relationships ──────────────

            // BudgetRequest → Department (no cascade)
            modelBuilder.Entity<BudgetRequest>()
                .HasOne(br => br.Department)
                .WithMany()
                .HasForeignKey(br => br.DepartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // BudgetRequest → Budget (no cascade)
            modelBuilder.Entity<BudgetRequest>()
                .HasOne(br => br.Budget)
                .WithMany()
                .HasForeignKey(br => br.BudgetID)
                .OnDelete(DeleteBehavior.Restrict);

            // BudgetRequest → Submitter (User, no cascade)
            modelBuilder.Entity<BudgetRequest>()
                .HasOne(br => br.Submitter)
                .WithMany()
                .HasForeignKey(br => br.SubmittedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // BudgetRequest → Approver (User, no cascade)
            modelBuilder.Entity<BudgetRequest>()
                .HasOne(br => br.Approver)
                .WithMany()
                .HasForeignKey(br => br.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // BudgetRequest default values for new columns (safe migration)
            modelBuilder.Entity<BudgetRequest>()
                .Property(br => br.Title)
                .HasDefaultValue("Budget Request");

            modelBuilder.Entity<BudgetRequest>()
                .Property(br => br.DateNeeded)
                .HasDefaultValueSql("GETDATE()");

            // ── Subscription relationships ──────────────

            // Subscription → Tenant (no cascade)
            modelBuilder.Entity<Subscription>()
                .HasOne(s => s.Tenant)
                .WithMany(t => t.Subscriptions)
                .HasForeignKey(s => s.TenantID)
                .OnDelete(DeleteBehavior.Restrict);

            // Subscription → Plan (no cascade)
            modelBuilder.Entity<Subscription>()
                .HasOne(s => s.Plan)
                .WithMany(p => p.Subscriptions)
                .HasForeignKey(s => s.PlanID)
                .OnDelete(DeleteBehavior.Restrict);

            // ── Payment relationships ──────────────────

            // Payment → Tenant (no cascade)
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Tenant)
                .WithMany(t => t.Payments)
                .HasForeignKey(p => p.TenantID)
                .OnDelete(DeleteBehavior.Restrict);

            // Payment → Subscription (cascade)
            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Subscription)
                .WithMany(s => s.Payments)
                .HasForeignKey(p => p.SubscriptionID)
                .OnDelete(DeleteBehavior.Restrict);

            // ── AuditLog relationships ───────────────

            // AuditLog → Tenant (no cascade)
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.Tenant)
                .WithMany()
                .HasForeignKey(a => a.TenantID)
                .OnDelete(DeleteBehavior.Restrict);

            // AuditLog → User (no cascade)
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // UserOTP → User (no cascade)
            modelBuilder.Entity<UserOTP>()
                .HasOne(o => o.User)
                .WithMany()
                .HasForeignKey(o => o.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // UserOTP → Tenant (no cascade)
            modelBuilder.Entity<UserOTP>()
                .HasOne(o => o.Tenant)
                .WithMany()
                .HasForeignKey(o => o.TenantID)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
