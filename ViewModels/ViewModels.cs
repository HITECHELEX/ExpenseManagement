using System.ComponentModel.DataAnnotations;
using ExpenseManagement.Models;

namespace ExpenseManagement.ViewModels
{
    public class LoginVM
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
        [Required]
        public string Password { get; set; } = "";
    }

    public class RegisterVM
    {
        [Required]
        public string FullName { get; set; } = "";
        [Required, EmailAddress]
        public string Email { get; set; } = "";
        [Required, MinLength(6)]
        public string Password { get; set; } = "";
        public UserRole Role { get; set; }
        public string? Department { get; set; }
    }

    public class AllocateBudgetVM
    {
        [Required]
        public int EmployeeId { get; set; }
        [Required, Range(1, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
        public decimal TotalAmount { get; set; }
        [Required]
        public string Purpose { get; set; } = "";
        public DateTime? ValidUntil { get; set; }
    }

    public class ExpenseClaimVM
    {
        [Required]
        public int BudgetId { get; set; }
        public int? ReportId { get; set; }
        [Required]
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        [Required]
        public string Category { get; set; } = "";
        [Required, Range(0.01, double.MaxValue, ErrorMessage = "Amount must be positive")]
        public decimal Amount { get; set; }
        public DateTime ExpenseDate { get; set; } = DateTime.Today;
        public IFormFile? Receipt { get; set; }
        public ExpenseManagement.Models.PaymentMethod PaymentMethod { get; set; } = ExpenseManagement.Models.PaymentMethod.Cash;
    }

    public class ReviewClaimVM
    {
        public int ClaimId { get; set; }
        public bool IsApproved { get; set; }
        public string? Remarks { get; set; }
    }

    public class CreateReportVM
    {
        [Required]
        public int BudgetId { get; set; }
        [Required]
        public string ReportTitle { get; set; } = "";
        public string? Summary { get; set; }
    }

    public class EmployeeDashboardVM
    {
        public User Employee { get; set; } = null!;
        public List<Budget> Budgets { get; set; } = new();
        public List<ExpenseClaim> RecentClaims { get; set; } = new();
        public List<ExpenseReport> DraftReports { get; set; } = new();
        public decimal TotalAllocated { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal TotalBalance { get; set; }
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int DeclinedCount { get; set; }
        public int UnreadNotifications { get; set; }
    }

    public class AccountDashboardVM
    {
        public int PendingClaims { get; set; }
        public int ApprovedClaims { get; set; }
        public int DeclinedClaims { get; set; }
        public int PendingReports { get; set; }
        public int TotalEmployees { get; set; }
        public decimal TotalAllocated { get; set; }
        public decimal TotalApproved { get; set; }
        public int TotalTopUps { get; set; }
        public decimal TotalTopUpAmount { get; set; }
        public List<ExpenseClaim> RecentPendingClaims { get; set; } = new();
        public List<ExpenseReport> PendingReports_List { get; set; } = new();
        public List<WalletTopUp> RecentTopUps { get; set; } = new();
        public int UnreadNotifications { get; set; }
    }

    public class ManagementDashboardVM
    {
        public int TotalEmployees { get; set; }
        public decimal TotalBudgetAllocated { get; set; }
        public decimal TotalSpent { get; set; }
        public int ReportsForReview { get; set; }
        public int ApprovedReports { get; set; }
        public int RejectedReports { get; set; }
        public List<ExpenseReport> RecentReports { get; set; } = new();
        public List<Budget> RecentBudgets { get; set; } = new();
        public int UnreadNotifications { get; set; }
    }

    public class ForwardReportVM
    {
        public int ReportId { get; set; }
        public string? Notes { get; set; }
    }

    public class ReviewReportVM
    {
        public int ReportId { get; set; }
        public bool IsApproved { get; set; }
        public string? Notes { get; set; }
        public string? Feedback { get; set; }
    }

    public class EditEmployeeVM
    {
        public int Id { get; set; }
        [Required]
        public string FullName { get; set; } = "";
        [Required, EmailAddress]
        public string Email { get; set; } = "";
        public string? Department { get; set; }
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters.")]
        public string? NewPassword { get; set; }
    }

    public class EmployeeWalletHistoryVM
    {
        public User Employee { get; set; } = null!;
        public decimal WalletBalance { get; set; }
        public decimal TotalAllocated { get; set; }
        public decimal TotalSpent { get; set; }
        public int TotalTopUps { get; set; }
        public decimal TotalTopUpAmount { get; set; }
        public List<WalletTopUp> TopUps { get; set; } = new();
        public List<Budget> Budgets { get; set; } = new();
    }

    public class EmployeeAllocationRow
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = "";
        public string Department { get; set; } = "";
        public decimal TotalAllocated { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal Remaining { get; set; }
        public int BudgetCount { get; set; }
        public int ReportCount { get; set; }
    }

    public class MonthlyAllocationRow
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string MonthLabel { get; set; } = "";
        public decimal TotalAdded { get; set; }
        public int TopUpCount { get; set; }
    }

    public class AllocationBreakdownVM
    {
        public decimal TotalAllocated { get; set; }
        public decimal TotalSpent { get; set; }
        public decimal TotalRemaining { get; set; }
        public List<EmployeeAllocationRow> ByEmployee { get; set; } = new();
        public List<MonthlyAllocationRow> ByMonth { get; set; } = new();
        public List<Budget> AllBudgets { get; set; } = new();
    }
}
