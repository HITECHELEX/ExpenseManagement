using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ExpenseManagement.Models;

namespace ExpenseManagement.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // PostgreSQL requires all DateTime values to be UTC.
        // This interceptor converts any Unspecified DateTime (e.g. from HTML form inputs)
        // to UTC automatically before every save — no need to fix every controller manually.
        public override int SaveChanges()
        {
            NormalizeDateTimeKinds();
            return base.SaveChanges();
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            NormalizeDateTimeKinds();
            return base.SaveChangesAsync(cancellationToken);
        }

        private void NormalizeDateTimeKinds()
        {
            foreach (var entry in ChangeTracker.Entries())
            {
                foreach (var property in entry.Properties)
                {
                    if (property.CurrentValue is DateTime dt && dt.Kind == DateTimeKind.Unspecified)
                        property.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
            }
        }

        public DbSet<Company> Companies => Set<Company>();
        public DbSet<User> Users => Set<User>();
        public DbSet<Budget> Budgets => Set<Budget>();
        public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();
        public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<WalletTopUp> WalletTopUps => Set<WalletTopUp>();
        public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
        public DbSet<OpeningBalance> OpeningBalances => Set<OpeningBalance>();
        public DbSet<PortalProfile> PortalProfiles => Set<PortalProfile>();

        // HRM
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<Attendance> Attendances => Set<Attendance>();
        public DbSet<Leave> Leaves => Set<Leave>();
        public DbSet<Payroll> Payrolls => Set<Payroll>();

        public DbSet<SalaryMaster> SalaryMasters => Set<SalaryMaster>();
        public DbSet<HRMConfiguration> HRMConfigurations => Set<HRMConfiguration>();
        public DbSet<Document> Documents => Set<Document>();
        public DbSet<Asset> Assets => Set<Asset>();
        public DbSet<AssetHistory> AssetHistories => Set<AssetHistory>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── DateTime UTC value converters ──────────────────────────────────────
            // PostgreSQL requires all DateTime to be UTC (timestamp with time zone).
            // These converters run at EF Core level — before Npgsql sees the value —
            // so any DateTime with Kind=Unspecified (e.g. from HTML form inputs) is
            // automatically treated as UTC. This is the version-agnostic fix that
            // works regardless of Npgsql version.
            var utcConverter = new ValueConverter<DateTime, DateTime>(
                v => v.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(v, DateTimeKind.Utc)
                    : v.ToUniversalTime(),
                v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            var nullableUtcConverter = new ValueConverter<DateTime?, DateTime?>(
                v => v == null ? v
                    : v.Value.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
                        : v.Value.ToUniversalTime(),
                v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime))
                        property.SetValueConverter(utcConverter);
                    else if (property.ClrType == typeof(DateTime?))
                        property.SetValueConverter(nullableUtcConverter);
                }
            }
            // ──────────────────────────────────────────────────────────────────────

            // Company-User relationship
            modelBuilder.Entity<User>()
                .HasOne(u => u.Company).WithMany(c => c.Users)
                .HasForeignKey(u => u.CompanyId).OnDelete(DeleteBehavior.Restrict);

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

            // WalletTopUp relationships
            modelBuilder.Entity<WalletTopUp>()
                .HasOne(w => w.Employee).WithMany()
                .HasForeignKey(w => w.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<WalletTopUp>()
                .HasOne(w => w.TopUpBy).WithMany()
                .HasForeignKey(w => w.TopUpById).OnDelete(DeleteBehavior.Restrict);

            // WalletTransaction relationships
            modelBuilder.Entity<WalletTransaction>()
                .HasOne(t => t.Employee).WithMany()
                .HasForeignKey(t => t.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<WalletTransaction>()
                .HasOne(t => t.PerformedBy).WithMany()
                .HasForeignKey(t => t.PerformedById).OnDelete(DeleteBehavior.ClientSetNull);

            // OpeningBalance relationships
            modelBuilder.Entity<OpeningBalance>()
                .HasOne(o => o.Employee).WithMany()
                .HasForeignKey(o => o.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<OpeningBalance>()
                .HasOne(o => o.AddedBy).WithMany()
                .HasForeignKey(o => o.AddedById).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<OpeningBalance>()
                .HasOne(o => o.Budget).WithMany()
                .HasForeignKey(o => o.BudgetId).OnDelete(DeleteBehavior.Restrict);
            // Index for quick lookup by employee
            modelBuilder.Entity<OpeningBalance>()
                .HasIndex(o => new { o.EmployeeId, o.AddedOn });

            // PortalProfile relationships
            modelBuilder.Entity<PortalProfile>()
                .HasOne(p => p.Company).WithMany()
                .HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<PortalProfile>()
                .HasIndex(p => new { p.Role, p.CompanyId, p.IsActive });

            // ── Performance Indexes ────────────────────────────────────────────────
            // Users: login lookup + role/company filters
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email).IsUnique();                          // Login: WHERE Email = ?
            modelBuilder.Entity<User>()
                .HasIndex(u => new { u.CompanyId, u.Role });                 // GetEmployeeIds: WHERE CompanyId = ? AND Role = ?

            // Budgets: most-used filter is EmployeeId + IsActive + IsLocked
            modelBuilder.Entity<Budget>()
                .HasIndex(b => b.EmployeeId);                                // All budget queries by employee
            modelBuilder.Entity<Budget>()
                .HasIndex(b => new { b.EmployeeId, b.IsActive });            // Dashboard: active budgets
            modelBuilder.Entity<Budget>()
                .HasIndex(b => new { b.EmployeeId, b.IsActive, b.IsLocked }); // AddExpense: active + unlocked

            // ExpenseClaims: filtered by employee, status, report, and budget
            modelBuilder.Entity<ExpenseClaim>()
                .HasIndex(c => c.EmployeeId);                                // Dashboard recent claims
            modelBuilder.Entity<ExpenseClaim>()
                .HasIndex(c => new { c.EmployeeId, c.Status });              // Status counts per employee
            modelBuilder.Entity<ExpenseClaim>()
                .HasIndex(c => c.ExpenseReportId);                           // Claims per report (include)
            modelBuilder.Entity<ExpenseClaim>()
                .HasIndex(c => c.BudgetId);                                  // Claims per budget
            modelBuilder.Entity<ExpenseClaim>()
                .HasIndex(c => new { c.EmployeeId, c.ExpenseDate });         // Ordered claim lists

            // ExpenseReports: filtered by employee + status (most frequent pattern)
            modelBuilder.Entity<ExpenseReport>()
                .HasIndex(r => r.EmployeeId);                                // All report queries by employee
            modelBuilder.Entity<ExpenseReport>()
                .HasIndex(r => r.Status);                                    // Status-based queries
            modelBuilder.Entity<ExpenseReport>()
                .HasIndex(r => new { r.EmployeeId, r.Status });              // Employee + status (dashboard, report list)
            modelBuilder.Entity<ExpenseReport>()
                .HasIndex(r => r.BudgetId);                                  // Reports per budget

            // Notifications: unread count is called on EVERY page load
            modelBuilder.Entity<Notification>()
                .HasIndex(n => n.UserId);                                    // All notifications by user
            modelBuilder.Entity<Notification>()
                .HasIndex(n => new { n.UserId, n.IsRead });                  // Unread count: WHERE UserId=? AND IsRead=false

            // WalletTransactions: always ordered by Date per employee
            modelBuilder.Entity<WalletTransaction>()
                .HasIndex(t => new { t.EmployeeId, t.Date });                // MyBudgets wallet history

            // WalletTopUps: always ordered by TopUpOn per employee
            modelBuilder.Entity<WalletTopUp>()
                .HasIndex(w => new { w.EmployeeId, w.TopUpOn });             // MyBudgets top-up history
            // ──────────────────────────────────────────────────────────────────────

            // Seed companies
            modelBuilder.Entity<Company>().HasData(
                new Company { Id = 1, Name = "HITECHELEXLLP", CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new Company { Id = 2, Name = "CEYONE SOLUTIONS PVT LTD", CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
            );

            // Seed default users (Management has no CompanyId — sees all companies)
            modelBuilder.Entity<User>().HasData(
                new User
                {
                    Id = 1,
                    FullName = "Admin Management",
                    Email = "management@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Management@123"),
                    Role = UserRole.Management,
                    Department = "Management",
                    CompanyId = null,
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                // HITECHELEXLLP
                new User
                {
                    Id = 2,
                    FullName = "Accounts Team",
                    Email = "accounts@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Accounts@123"),
                    Role = UserRole.AccountTeam,
                    Department = "Finance",
                    CompanyId = 1,
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new User
                {
                    Id = 3,
                    FullName = "John Employee",
                    Email = "employee@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Employee@123"),
                    Role = UserRole.Employee,
                    Department = "Sales",
                    CompanyId = 1,
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                // CEYONE SOLUTIONS PVT LTD
                new User
                {
                    Id = 4,
                    FullName = "Ceyone Accounts",
                    Email = "accounts.ceyone@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("CeyoneAcc@123"),
                    Role = UserRole.AccountTeam,
                    Department = "Finance",
                    CompanyId = 2,
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                new User
                {
                    Id = 5,
                    FullName = "Ceyone Employee",
                    Email = "employee.ceyone@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("CeyoneEmp@123"),
                    Role = UserRole.Employee,
                    Department = "Operations",
                    CompanyId = 2,
                    CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }

            );

            // Seed portal profiles — Account Team members per company + Management directors
            modelBuilder.Entity<PortalProfile>().HasData(
                // HITECHELEXLLP Account Team
                new PortalProfile { Id = 1, Name = "Ankit", Role = UserRole.AccountTeam, CompanyId = 1, IsActive = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PortalProfile { Id = 2, Name = "Avinash", Role = UserRole.AccountTeam, CompanyId = 1, IsActive = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                // CEYONE SOLUTIONS PVT LTD Account Team
                new PortalProfile { Id = 3, Name = "Kailash", Role = UserRole.AccountTeam, CompanyId = 2, IsActive = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                // Management Directors (global — CompanyId null not allowed in seed; use 0 convention then handle in query)
                // Note: CompanyId=null cannot be seeded with HasData; profiles are inserted via migration SQL instead
                new PortalProfile { Id = 4, Name = "Suvarna Bhutkar", Role = UserRole.Management, CompanyId = null, IsActive = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PortalProfile { Id = 5, Name = "Ajay Bhutkar", Role = UserRole.Management, CompanyId = null, IsActive = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
                new PortalProfile { Id = 6, Name = "Vilas Bhosale", Role = UserRole.Management, CompanyId = null, IsActive = true, CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
            );

            // ============================================================
            // HRM RELATIONSHIPS
            // ============================================================


            // ------------------------------------------------------------
            // Users → Employees
            //
            // One User can have zero or one Employee HR record.
            // Employee.UserId is UNIQUE.
            // ------------------------------------------------------------

            modelBuilder.Entity<Employee>()
                .HasOne(e => e.User)
                .WithOne(u => u.EmployeeProfile)
                .HasForeignKey<Employee>(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);


            // ------------------------------------------------------------
            // Employees → Attendance
            //
            // One Employee can have many Attendance records.
            // ------------------------------------------------------------

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.Employee)
                .WithMany(e => e.Attendances)
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Attendance>()
    .HasOne<User>()
    .WithMany()
    .HasForeignKey(a => a.LateReviewedBy)
    .OnDelete(DeleteBehavior.Restrict);

            // ------------------------------------------------------------
            // Employees → Leave
            //
            // One Employee can have many Leave records.
            // ------------------------------------------------------------

            modelBuilder.Entity<Leave>()
                .HasOne(l => l.Employee)
                .WithMany(e => e.Leaves)
                .HasForeignKey(l => l.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);


            // ------------------------------------------------------------
            // Employees → Payroll
            //
            // One Employee can have many Payroll records.
            // ------------------------------------------------------------

            modelBuilder.Entity<Payroll>()
                .HasOne(p => p.Employee)
                .WithMany(e => e.Payrolls)
                .HasForeignKey(p => p.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);


            // ------------------------------------------------------------
            // Employees → Employees
            //
            // Reporting manager self-reference.
            // ------------------------------------------------------------

            modelBuilder.Entity<Employee>()
                .HasOne(e => e.ReportingManager)
                .WithMany(e => e.DirectReports)
                .HasForeignKey(e => e.ReportingManagerId)
                .OnDelete(DeleteBehavior.Restrict);


            // ------------------------------------------------------------
            // Users → Attendance approvals
            // ------------------------------------------------------------

            modelBuilder.Entity<Attendance>()
                .HasOne(a => a.ApprovedByUser)
                .WithMany(u => u.AttendanceApprovals)
                .HasForeignKey(a => a.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);


            // ------------------------------------------------------------
            // Users → Leave reviews
            // ------------------------------------------------------------

            modelBuilder.Entity<Leave>()
                .HasOne(l => l.ReviewedByUser)
                .WithMany(u => u.LeaveReviews)
                .HasForeignKey(l => l.ReviewedBy)
                .OnDelete(DeleteBehavior.Restrict);


            // ------------------------------------------------------------
            // Users → Payroll approvals
            // ------------------------------------------------------------

            modelBuilder.Entity<Payroll>()
                .HasOne(p => p.ApprovedByUser)
                .WithMany(u => u.PayrollApprovals)
                .HasForeignKey(p => p.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);


            // ============================================================
            // HRM UNIQUE CONSTRAINTS / INDEXES
            // ============================================================

            // One HRM employee record per User
            modelBuilder.Entity<Employee>()
                .HasIndex(e => e.UserId)
                .IsUnique();

            // EmployeeCode must be unique
            modelBuilder.Entity<Employee>()
                .HasIndex(e => e.EmployeeCode)
                .IsUnique();

            // One attendance record per employee per date
            modelBuilder.Entity<Attendance>()
                .HasIndex(a => new { a.EmployeeId, a.AttendanceDate })
                .IsUnique();

            // One payroll record per employee per month/year
            modelBuilder.Entity<Payroll>()
                .HasIndex(p => new { p.EmployeeId, p.Month, p.Year })
                .IsUnique();

            // Useful HRM lookup indexes
            modelBuilder.Entity<Attendance>()
                .HasIndex(a => a.AttendanceDate);

            modelBuilder.Entity<Leave>()
                .HasIndex(l => new { l.EmployeeId, l.FromDate, l.ToDate });

            modelBuilder.Entity<Payroll>()
                .HasIndex(p => new { p.Year, p.Month });


            modelBuilder.Entity<SalaryMaster>()
    .HasOne(s => s.Employee)
    .WithMany()
    .HasForeignKey(s => s.EmployeeId)
    .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<SalaryMaster>()
    .HasIndex(s => s.EmployeeId)
    .IsUnique()
    .HasFilter(@"""IsActive"" = true");

            modelBuilder.Entity<SalaryMaster>()
                .Property(s => s.BasicSalary)
                .HasColumnType("numeric(12,2)");

            modelBuilder.Entity<SalaryMaster>()
                .Property(s => s.HRA)
                .HasColumnType("numeric(12,2)");

            modelBuilder.Entity<SalaryMaster>()
                .Property(s => s.ConveyanceAllowance)
                .HasColumnType("numeric(12,2)");

            modelBuilder.Entity<SalaryMaster>()
                .Property(s => s.WashingAllowance)
                .HasColumnType("numeric(12,2)");

            modelBuilder.Entity<SalaryMaster>()
                .Property(s => s.OtherAllowances)
                .HasColumnType("numeric(12,2)");


            // ============================================================
            // FINAL HRM SCHEMA
            // ============================================================

            modelBuilder.Entity<Attendance>()
                .HasIndex(a => a.ShiftCode);

            modelBuilder.Entity<Attendance>()
                .Property(a => a.BiometricPunchesJson)
                .HasColumnType("jsonb");

            modelBuilder.Entity<HRMConfiguration>()
                .HasOne(c => c.Company)
                .WithMany()
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<HRMConfiguration>()
                .HasIndex(c => new
                {
                    c.CompanyId,
                    c.ConfigurationType,
                    c.ConfigurationCode,
                    c.Version
                })
                .IsUnique();

            modelBuilder.Entity<HRMConfiguration>()
                .HasIndex(c => new
                {
                    c.CompanyId,
                    c.ConfigurationType,
                    c.ConfigurationCode,
                    c.IsActive
                });

            modelBuilder.Entity<HRMConfiguration>()
                .Property(c => c.ConfigurationJson)
                .HasColumnType("jsonb");

            modelBuilder.Entity<Document>()
                .HasOne(d => d.Company)
                .WithMany()
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Document>()
                .HasOne(d => d.Employee)
                .WithMany()
                .HasForeignKey(d => d.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Document>()
                .HasOne(d => d.UploadedByUser)
                .WithMany()
                .HasForeignKey(d => d.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Document>()
                .HasIndex(d => new { d.CompanyId, d.EmployeeId });

            modelBuilder.Entity<Document>()
                .HasIndex(d => new { d.CompanyId, d.DocumentKind });

            modelBuilder.Entity<Asset>()
                .HasOne(a => a.Company)
                .WithMany()
                .HasForeignKey(a => a.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Asset>()
                .HasOne(a => a.CurrentEmployee)
                .WithMany()
                .HasForeignKey(a => a.CurrentEmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Asset>()
                .HasIndex(a => new { a.CompanyId, a.AssetCode })
                .IsUnique();

            modelBuilder.Entity<Asset>()
                .HasIndex(a => a.CurrentEmployeeId);

            modelBuilder.Entity<AssetHistory>()
                .HasOne(h => h.Asset)
                .WithMany()
                .HasForeignKey(h => h.AssetId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetHistory>()
                .HasOne(h => h.Employee)
                .WithMany()
                .HasForeignKey(h => h.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetHistory>()
                .HasOne(h => h.PerformedByUser)
                .WithMany()
                .HasForeignKey(h => h.PerformedBy)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetHistory>()
                .HasIndex(h => new { h.AssetId, h.ActionDate });

            modelBuilder.Entity<AssetHistory>()
                .HasIndex(h => h.EmployeeId);

            modelBuilder.Entity<AssetHistory>()
                .HasIndex(h => h.PerformedBy);

            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.Company)
                .WithMany()
                .HasForeignKey(a => a.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.CompanyId, a.CreatedAt });

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.UserId, a.CreatedAt });

            modelBuilder.Entity<AuditLog>()
                .HasIndex(a => new { a.EntityName, a.EntityId });
        }
    }
}
