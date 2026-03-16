using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Models;

namespace ExpenseManagement.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Budget> Budgets => Set<Budget>();
        public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();
        public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();
        public DbSet<Notification> Notifications => Set<Notification>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Budget relationships
            modelBuilder.Entity<Budget>()
                .HasOne(b => b.Employee).WithMany(u => u.Budgets)
                .HasForeignKey(b => b.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Budget>()
                .HasOne(b => b.AllocatedBy).WithMany()
                .HasForeignKey(b => b.AllocatedById).OnDelete(DeleteBehavior.Restrict);

            // ExpenseClaim relationships
            modelBuilder.Entity<ExpenseClaim>()
                .HasOne(e => e.Employee).WithMany(u => u.ExpenseClaims)
                .HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ExpenseClaim>()
                .HasOne(e => e.ReviewedBy).WithMany()
                .HasForeignKey(e => e.ReviewedById).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ExpenseClaim>()
                .HasOne(e => e.Budget).WithMany(b => b.ExpenseClaims)
                .HasForeignKey(e => e.BudgetId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ExpenseClaim>()
                .HasOne(e => e.ExpenseReport).WithMany(r => r.ExpenseClaims)
                .HasForeignKey(e => e.ExpenseReportId).OnDelete(DeleteBehavior.NoAction);

            // ExpenseReport relationships
            modelBuilder.Entity<ExpenseReport>()
                .HasOne(r => r.Employee).WithMany(u => u.ExpenseReports)
                .HasForeignKey(r => r.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ExpenseReport>()
                .HasOne(r => r.Budget).WithMany(b => b.ExpenseReports)
                .HasForeignKey(r => r.BudgetId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ExpenseReport>()
                .HasOne(r => r.AccountTeamVerifiedBy).WithMany()
                .HasForeignKey(r => r.AccountTeamVerifiedById).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ExpenseReport>()
                .HasOne(r => r.ManagementReviewedBy).WithMany()
                .HasForeignKey(r => r.ManagementReviewedById).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<ExpenseReport>()
                .HasOne(r => r.TransferredToBudget).WithMany()
                .HasForeignKey(r => r.TransferredToBudgetId).OnDelete(DeleteBehavior.SetNull);
            // IsLocked and LockedReason are simple scalar columns — no relationship config needed

            // Notification relationship
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User).WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Cascade);

            // Seed default users
            modelBuilder.Entity<User>().HasData(
                new User { Id = 1, FullName = "Admin Management", Email = "management@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Management@123"),
                    Role = UserRole.Management, Department = "Management", CreatedAt = new DateTime(2024,1,1) },
                new User { Id = 2, FullName = "Accounts Head", Email = "accounts@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Accounts@123"),
                    Role = UserRole.AccountTeam, Department = "Finance", CreatedAt = new DateTime(2024,1,1) },
                new User { Id = 3, FullName = "John Employee", Email = "employee@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Employee@123"),
                    Role = UserRole.Employee, Department = "Sales", CreatedAt = new DateTime(2024,1,1) }
            );
        }
    }
}
