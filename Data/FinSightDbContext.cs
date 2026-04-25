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

            // Expense → User (no cascade)
            modelBuilder.Entity<Expense>()
                .HasOne(e => e.Creator)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
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
        }
    }
}
