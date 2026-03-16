using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExpenseManagement.Models
{
    public enum UserRole { Employee, AccountTeam, Management }
    public enum ExpenseStatus { Pending, Approved, Declined }
    public enum ReportStatus
    {
        Draft,
        SubmittedToAccountTeam,
        ReviewedByAccountTeam,
        ForwardedToManagement,
        ApprovedByManagement,
        RejectedByManagement
    }

    public class User
    {
        public int Id { get; set; }
        [Required, MaxLength(100)]
        public string FullName { get; set; } = "";
        [Required, MaxLength(150)]
        public string Email { get; set; } = "";
        [Required]
        public string PasswordHash { get; set; } = "";
        public UserRole Role { get; set; }
        public string? Department { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
        public ICollection<ExpenseClaim> ExpenseClaims { get; set; } = new List<ExpenseClaim>();
        public ICollection<ExpenseReport> ExpenseReports { get; set; } = new List<ExpenseReport>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    }

    public class Budget
    {
        public int Id { get; set; }

        [ForeignKey("Employee")]
        public int EmployeeId { get; set; }
        public User? Employee { get; set; }

        [ForeignKey("AllocatedBy")]
        public int AllocatedById { get; set; }
        public User? AllocatedBy { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SpentAmount { get; set; } = 0;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ReturnedBalance { get; set; } = 0;

        // Cumulative overspend on this budget — cleared when next budget is allocated
        [Column(TypeName = "decimal(18,2)")]
        public decimal OverspentAmount { get; set; } = 0;

        // RemainingBalance CAN go negative (overspend allowed)
        [NotMapped]
        public decimal RemainingBalance => TotalAmount - SpentAmount;

        [NotMapped]
        public bool IsOverspent => RemainingBalance < 0;

        public string Purpose { get; set; } = "";
        public DateTime AllocatedOn { get; set; } = DateTime.Now;
        public DateTime? ValidUntil { get; set; }
        public bool IsActive { get; set; } = true;

        // True = a report tied to this budget was approved AND balance was transferred back
        // When true, employee can no longer create NEW reports against this budget
        public bool IsLocked { get; set; } = false;

        // Label shown when budget is locked / balance returned
        public string? LockedReason { get; set; }

        public ICollection<ExpenseClaim> ExpenseClaims { get; set; } = new List<ExpenseClaim>();
        public ICollection<ExpenseReport> ExpenseReports { get; set; } = new List<ExpenseReport>();
    }

    public class ExpenseClaim
    {
        public int Id { get; set; }
        [ForeignKey("Employee")]
        public int EmployeeId { get; set; }
        public User? Employee { get; set; }
        [ForeignKey("Budget")]
        public int BudgetId { get; set; }
        public Budget? Budget { get; set; }
        public int? ExpenseReportId { get; set; }
        public ExpenseReport? ExpenseReport { get; set; }
        [Required, MaxLength(200)]
        public string Title { get; set; } = "";
        [MaxLength(500)]
        public string Description { get; set; } = "";
        [Required, MaxLength(100)]
        public string Category { get; set; } = "";
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }
        public string? ReceiptPath { get; set; }
        public string? ReceiptFileName { get; set; }
        public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;
        public string? AccountTeamRemarks { get; set; }
        [ForeignKey("ReviewedBy")]
        public int? ReviewedById { get; set; }
        public User? ReviewedBy { get; set; }
        public DateTime? ReviewedOn { get; set; }
        public DateTime SubmittedOn { get; set; } = DateTime.Now;
        public DateTime ExpenseDate { get; set; } = DateTime.Today;
    }

    public class ExpenseReport
    {
        public int Id { get; set; }
        [ForeignKey("Employee")]
        public int EmployeeId { get; set; }
        public User? Employee { get; set; }
        [ForeignKey("Budget")]
        public int BudgetId { get; set; }
        public Budget? Budget { get; set; }
        public string ReportTitle { get; set; } = "";
        public string? Summary { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalClaimed { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalApproved { get; set; }

        // Positive = unused balance returned to employee.
        // Negative = employee overspent (will be deducted from next allocation).
        [Column(TypeName = "decimal(18,2)")]
        public decimal RemainingBalanceAtSubmission { get; set; } = 0;

        public ReportStatus Status { get; set; } = ReportStatus.Draft;
        public string? AccountTeamNotes { get; set; }
        public string? ManagementNotes { get; set; }
        public string? ManagementFeedback { get; set; }

        [ForeignKey("AccountTeamVerifiedBy")]
        public int? AccountTeamVerifiedById { get; set; }
        public User? AccountTeamVerifiedBy { get; set; }
        public DateTime? AccountTeamVerifiedOn { get; set; }

        [ForeignKey("ManagementReviewedBy")]
        public int? ManagementReviewedById { get; set; }
        public User? ManagementReviewedBy { get; set; }
        public DateTime? ManagementReviewedOn { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.Now;
        public DateTime? ForwardedToAccountTeamOn { get; set; }

        // Balance transfer back to employee after management approval
        public bool BalanceTransferred { get; set; } = false;
        public DateTime? BalanceTransferredOn { get; set; }

        // Which budget received the returned balance
        [ForeignKey("TransferredToBudget")]
        public int? TransferredToBudgetId { get; set; }
        public Budget? TransferredToBudget { get; set; }

        public ICollection<ExpenseClaim> ExpenseClaims { get; set; } = new List<ExpenseClaim>();
    }

    public class Notification
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public string Message { get; set; } = "";
        public string? Link { get; set; }
        public string Icon { get; set; } = "bell";
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
