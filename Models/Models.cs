using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExpenseManagement.Models
{
    public enum UserRole { Employee, AccountTeam, Management, HR }
    public enum ExpenseStatus { Pending, Approved, Declined }
    public enum PaymentMethod { Cash, UPI }
    public enum WalletTransactionType { Credit, Debit }
    public enum ReportStatus
    {
        Draft,
        SubmittedToAccountTeam,
        ReviewedByAccountTeam,
        ForwardedToManagement,
        ApprovedByManagement,
        RejectedByManagement,
        RejectedByAccountTeam
    }

    public class Company
    {
        public int Id { get; set; }
        [Required, MaxLength(200)]
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<User> Users { get; set; } = new List<User>();
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

        // Null for Management (sees all companies)
        public int? CompanyId { get; set; }
        public Company? Company { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ICollection<Budget> Budgets { get; set; } = new List<Budget>();
        public ICollection<ExpenseClaim> ExpenseClaims { get; set; } = new List<ExpenseClaim>();
        public ICollection<ExpenseReport> ExpenseReports { get; set; } = new List<ExpenseReport>();
        public ICollection<Notification> Notifications { get; set; } = new List<Notification>();

        // HRM
        public Employee? EmployeeProfile { get; set; }

        public ICollection<Attendance> AttendanceApprovals { get; set; }
            = new List<Attendance>();

        public ICollection<Leave> LeaveReviews { get; set; }
            = new List<Leave>();

        public ICollection<Payroll> PayrollApprovals { get; set; }
            = new List<Payroll>();
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
        public DateTime AllocatedOn { get; set; } = DateTime.UtcNow;
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
        // Additional receipt photos (up to 3 total)
        public string? Receipt2Path { get; set; }
        public string? Receipt2FileName { get; set; }
        public string? Receipt3Path { get; set; }
        public string? Receipt3FileName { get; set; }
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
        public ExpenseStatus Status { get; set; } = ExpenseStatus.Pending;
        public string? AccountTeamRemarks { get; set; }
        [ForeignKey("ReviewedBy")]
        public int? ReviewedById { get; set; }
        public User? ReviewedBy { get; set; }
        public DateTime? ReviewedOn { get; set; }
        public DateTime SubmittedOn { get; set; } = DateTime.UtcNow;
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

        // Account Team member who reviewed — selected at AT login, stored permanently
        public string? AccountTeamProfileName { get; set; }

        // Director who approved/rejected — selected at login, stored with report permanently
        public string? DirectorName { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ForwardedToAccountTeamOn { get; set; }

        // Date the employee received the funds (selected at submission, shown in Excel report)
        public DateTime? FundsReceivedOn { get; set; }

        // Balance transfer back to employee after management approval
        public bool BalanceTransferred { get; set; } = false;
        public DateTime? BalanceTransferredOn { get; set; }

        // Which budget received the returned balance
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
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Carry-forward / opening balance entry added by Account Team.
    /// Positive Amount = credit (unused balance from prev period).
    /// Negative Amount = debit (overspend carried forward as debt).
    /// Adjusts Budget.TotalAmount (positive) or Budget.SpentAmount (negative)
    /// so ALL existing balance calculations remain untouched.
    /// </summary>
    public class OpeningBalance
    {
        public int Id { get; set; }

        [ForeignKey("Employee")]
        public int EmployeeId { get; set; }
        public User? Employee { get; set; }

        [ForeignKey("AddedBy")]
        public int AddedById { get; set; }
        public User? AddedBy { get; set; }

        [ForeignKey("Budget")]
        public int BudgetId { get; set; }
        public Budget? Budget { get; set; }

        // Positive = carry-forward credit; Negative = carry-forward debt
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        // Human-readable period label, e.g. "April 2026" or "FY 2025-26"
        public string Period { get; set; } = "";

        public string? Remarks { get; set; }

        // Proof document stored on S3 (required)
        public string? ProofDocumentPath { get; set; }
        public string? ProofDocumentFileName { get; set; }

        public DateTime AddedOn { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// A named person (Ankit, Avinash, Kailash…) who can be selected when an Account Team or
    /// Management user logs in. Their name is stored on each report they action so Excel exports
    /// and report timelines always show the real person, not the generic account name.
    /// AccountTeam profiles are company-scoped; Management profiles are global (CompanyId = null).
    /// </summary>
    public class PortalProfile
    {
        public int Id { get; set; }
        [Required, MaxLength(150)]
        public string Name { get; set; } = "";
        public UserRole Role { get; set; }          // AccountTeam or Management
        public int? CompanyId { get; set; }         // null = global (Management)
        public Company? Company { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class WalletTopUp
    {
        public int Id { get; set; }

        [ForeignKey("Employee")]
        public int EmployeeId { get; set; }
        public User? Employee { get; set; }

        [ForeignKey("TopUpBy")]
        public int TopUpById { get; set; }
        public User? TopUpBy { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public string Purpose { get; set; } = "";

        public DateTime TopUpOn { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceAfter { get; set; }
    }

    public class WalletTransaction
    {
        public int Id { get; set; }

        [ForeignKey("Employee")]
        public int EmployeeId { get; set; }
        public User? Employee { get; set; }

        [ForeignKey("PerformedBy")]
        public int? PerformedById { get; set; }
        public User? PerformedBy { get; set; }

        public WalletTransactionType Type { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        public string Description { get; set; } = "";

        public DateTime Date { get; set; } = DateTime.UtcNow;

        [Column(TypeName = "decimal(18,2)")]
        public decimal BalanceAfter { get; set; }
    }


    // ============================================================
    // HRM - EMPLOYEE
    // ============================================================

    public class Employee
    {
        public int Id { get; set; }

        // Link to existing Users table
        [Required]
        public int UserId { get; set; }

        public User? User { get; set; }

        // Employee identification
        [Required, MaxLength(50)]
        public string EmployeeCode { get; set; } = "";

        // Personal information
        public DateTime? DateOfBirth { get; set; }

        [MaxLength(50)]
        public string? Gender { get; set; }

        [MaxLength(30)]
        public string? Phone { get; set; }

        [MaxLength(30)]
        public string? AlternatePhone { get; set; }

        [MaxLength(30)]
        public string? EmergencyContactPhone { get; set; }

        [MaxLength(150)]
        public string? PersonalEmail { get; set; }

        [MaxLength(20)]
        public string? AadhaarNumber { get; set; }

        [MaxLength(20)]
        public string? PANNumber { get; set; }

        public string? MedicalHistory { get; set; }

        // Address
        public string? PresentAddress { get; set; }

        public string? AadhaarAddress { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(100)]
        public string? State { get; set; }

        [MaxLength(20)]
        public string? Pincode { get; set; }

        // Emergency contact
        [MaxLength(150)]
        public string? EmergencyContactName { get; set; }

        // Employment
        public DateTime? JoiningDate { get; set; }

        [MaxLength(150)]
        public string? Designation { get; set; }

        // Self-referencing reporting manager
        public int? ReportingManagerId { get; set; }

        public Employee? ReportingManager { get; set; }

        public ICollection<Employee> DirectReports { get; set; }
            = new List<Employee>();

        [MaxLength(50)]
        public string? EmploymentStatus { get; set; }

        [MaxLength(50)]
        public string? EmploymentType { get; set; }

        public DateTime? ExitDate { get; set; }

        public string? ExitReason { get; set; }

        // Employee category flags
        public bool IsIDBI { get; set; } = false;
        public bool IsNonIDBI { get; set; } = false;
        public bool IsOnSite { get; set; } = false;

        // Salary bank account
        [MaxLength(150)]
        public string? BankName { get; set; }

        [MaxLength(150)]
        public string? BankBranch { get; set; }

        [MaxLength(50)]
        public string? BankAccountNumber { get; set; }

        [MaxLength(50)]
        public string? BankIFSCCode { get; set; }

        [MaxLength(150)]
        public string? BankAccountHolderName { get; set; }

        // Onboarding
        [MaxLength(30)]
        public string? OnboardingStatus { get; set; }

        public DateTime? OnboardingStartedOn { get; set; }
        public DateTime? OnboardingCompletedOn { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // HRM transactions
        public ICollection<Attendance> Attendances { get; set; }
            = new List<Attendance>();

        public ICollection<Leave> Leaves { get; set; }
            = new List<Leave>();

        public ICollection<Payroll> Payrolls { get; set; }
            = new List<Payroll>();
    }


    // ============================================================
    // HRM - ATTENDANCE
    // ============================================================

    public class Attendance
    {
        public int Id { get; set; }

        // Employee
        [Required]
        public int EmployeeId { get; set; }

        public Employee? Employee { get; set; }

        // Daily attendance
        [Required]
        public DateTime AttendanceDate { get; set; }

        public DateTime? CheckIn { get; set; }

        public DateTime? CheckOut { get; set; }

        [MaxLength(30)]
        public string? Status { get; set; }

        public int? LateMinutes { get; set; }

        public int? EarlyLeaveMinutes { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? WorkingHours { get; set; }

        // Attendance source
        [MaxLength(50)]
        public string? Source { get; set; }

        // Original punch
        public DateTime? OriginalCheckIn { get; set; }

        public DateTime? OriginalCheckOut { get; set; }

        // Correction
        public bool IsCorrectionRequested { get; set; } = false;

        public DateTime? CorrectionRequestedOn { get; set; }

        public string? CorrectionRemarks { get; set; }

        [MaxLength(30)]
        public string? CorrectionStatus { get; set; }

        // Correction approval
        public int? ApprovedBy { get; set; }

        public User? ApprovedByUser { get; set; }

        public DateTime? ApprovedOn { get; set; }

        // General remarks
        public string? Remarks { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ============================================================
        // Attendance Mode
        // ============================================================

        [MaxLength(20)]
        public string? AttendanceMode { get; set; }

        [MaxLength(30)]
        public string? ShiftCode { get; set; }

        [Column(TypeName = "numeric(9,6)")]
        public decimal? CheckInLatitude { get; set; }

        [Column(TypeName = "numeric(9,6)")]
        public decimal? CheckInLongitude { get; set; }

        [Column(TypeName = "numeric(8,2)")]
        public decimal? CheckInAccuracy { get; set; }

        public string? CheckInLocationName { get; set; }

        [Column(TypeName = "numeric(9,6)")]
        public decimal? CheckOutLatitude { get; set; }

        [Column(TypeName = "numeric(9,6)")]
        public decimal? CheckOutLongitude { get; set; }

        [Column(TypeName = "numeric(8,2)")]
        public decimal? CheckOutAccuracy { get; set; }

        public string? CheckOutLocationName { get; set; }

        [MaxLength(200)]
        public string? BiometricImportReference { get; set; }

        [Column(TypeName = "jsonb")]
        public string? BiometricPunchesJson { get; set; }


        // ============================================================
        // Late Mark
        // ============================================================

        public bool IsLate { get; set; } = false;

        public int? LateMarkNumber { get; set; }

        public string? LateReason { get; set; }


        // ============================================================
        // Late Review
        // ============================================================

        [MaxLength(20)]
        public string? LateReviewStatus { get; set; }

        public string? LateReviewRemarks { get; set; }

        public int? LateReviewedBy { get; set; }

        public DateTime? LateReviewedOn { get; set; }


        // ============================================================
        // Visit Attendance
        // ============================================================

        public string? VisitLocation { get; set; }
    }


    // ============================================================
    // HRM - LEAVE
    // ============================================================

    public class Leave
    {
        public int Id { get; set; }

        // Employee
        [Required]
        public int EmployeeId { get; set; }

        public Employee? Employee { get; set; }

        // Leave information
        [Required, MaxLength(50)]
        public string LeaveType { get; set; } = "";

        [Required]
        public DateTime FromDate { get; set; }

        [Required]
        public DateTime ToDate { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? TotalDays { get; set; }

        public string? Reason { get; set; }

        // Leave balance snapshot
        [Column(TypeName = "decimal(5,2)")]
        public decimal? LeaveBalanceBefore { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? LeaveBalanceAfter { get; set; }

        // Status
        [MaxLength(30)]
        public string? Status { get; set; }

        public DateTime? AppliedOn { get; set; }

        // HR review
        public int? ReviewedBy { get; set; }

        public User? ReviewedByUser { get; set; }

        public DateTime? ReviewedOn { get; set; }

        public string? HRRemarks { get; set; }

        // Cancellation
        public DateTime? CancelledOn { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }


    // ============================================================
    // HRM - PAYROLL
    // ============================================================

    public class Payroll
    {
        public int Id { get; set; }

        // Employee
        [Required]
        public int EmployeeId { get; set; }

        public Employee? Employee { get; set; }

        // Payroll period
        [Required]
        public int Month { get; set; }

        [Required]
        public int Year { get; set; }

        public DateTime? PayrollPeriodFrom { get; set; }
        public DateTime? PayrollPeriodTo { get; set; }

        [MaxLength(50)]
        public string? PayslipNumber { get; set; }

        // Salary structure
        [Column(TypeName = "decimal(12,2)")]
        public decimal? BasicSalary { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? HRA { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? ConveyanceAllowance { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? WashingAllowance { get; set; }


        [Column(TypeName = "decimal(12,2)")]
        public decimal? OtherAllowances { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? PerformanceBonus { get; set; }

        // Earnings
        [Column(TypeName = "decimal(12,2)")]
        public decimal? GrossSalary { get; set; }

        // Deductions
        [Column(TypeName = "decimal(12,2)")]
        public decimal? PF { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? ESI { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? ProfessionalTax { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? LWF { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? OtherDeductions { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? TotalDeductions { get; set; }

        // Final salary
        [Column(TypeName = "decimal(12,2)")]
        public decimal? NetSalary { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? EmployerPF { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? EmployerESIC { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? EmployerLWF { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal? EmployerOtherContribution { get; set; }

        // Attendance-based calculation
        [Column(TypeName = "decimal(5,2)")]
        public decimal? WorkingDays { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? PayDays { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? LOP { get; set; }

        [Column(TypeName = "decimal(5,2)")]
        public decimal? PaidDays { get; set; }
        // Approval
        public int? ApprovedBy { get; set; }

        public User? ApprovedByUser { get; set; }

        public DateTime? ApprovedOn { get; set; }

        // Status
        [MaxLength(30)]
        public string? Status { get; set; }

        [MaxLength(30)]
        public string? PaymentStatus { get; set; }

        public DateTime? PaymentDate { get; set; }

        [MaxLength(150)]
        public string? PaymentReference { get; set; }

        [MaxLength(150)]
        public string? BankFileReference { get; set; }

        public DateTime? PaidOn { get; set; }

        public string? Remarks { get; set; }

        // Audit
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }


    public class SalaryMaster
    {
        public int Id { get; set; }

        public int EmployeeId { get; set; }
        public Employee Employee { get; set; } = null!;

        // Employee statutory information
        [MaxLength(20)]
        public string? PANNumber { get; set; }

        [MaxLength(30)]
        public string? UANNumber { get; set; }

        [MaxLength(30)]
        public string? ESICNumber { get; set; }

        // Monthly salary components
        [Column(TypeName = "numeric(12,2)")]
        public decimal BasicSalary { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal HRA { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal ConveyanceAllowance { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal WashingAllowance { get; set; }

        [Column(TypeName = "numeric(12,2)")]
        public decimal OtherAllowances { get; set; }

        // Statutory applicability
        public bool PFApplicable { get; set; } = true;

        public bool ESICApplicable { get; set; } = true;

        public bool ProfessionalTaxApplicable { get; set; } = true;

        public bool LWFApplicable { get; set; } = true;

        // Salary revision
        public DateTime EffectiveFrom { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // ============================================================
    // HRM - CONFIGURATION
    // ============================================================

    public class HRMConfiguration
    {
        public int Id { get; set; }

        [Required]
        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        [Required, MaxLength(50)]
        public string ConfigurationType { get; set; } = "";

        [Required, MaxLength(100)]
        public string ConfigurationCode { get; set; } = "";

        [Required, Column(TypeName = "jsonb")]
        public string ConfigurationJson { get; set; } = "{}";

        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo { get; set; }

        public int Version { get; set; } = 1;
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }


    // ============================================================
    // HRM - DOCUMENTS
    // ============================================================

    public class Document
    {
        public int Id { get; set; }

        [Required]
        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        public int? EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        [Required, MaxLength(30)]
        public string DocumentKind { get; set; } = "";

        [MaxLength(50)]
        public string? DocumentCategory { get; set; }

        [MaxLength(100)]
        public string? DocumentType { get; set; }

        [Required, MaxLength(200)]
        public string DocumentName { get; set; } = "";

        [MaxLength(100)]
        public string? TemplateCode { get; set; }

        public int? TemplateVersion { get; set; }
        public string? TemplateContent { get; set; }

        [MaxLength(255)]
        public string? FileName { get; set; }
        public string? FilePath { get; set; }

        [MaxLength(30)]
        public string? DocumentStatus { get; set; }

        public bool IsOnboardingDocument { get; set; } = false;
        public bool RequiresSignature { get; set; } = false;

        [MaxLength(30)]
        public string? SignatureStatus { get; set; }

        public DateTime? SignedOn { get; set; }

        [MaxLength(200)]
        public string? SignatureReference { get; set; }

        public int? UploadedBy { get; set; }
        public User? UploadedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }


    // ============================================================
    // HRM - ASSETS
    // ============================================================

    public class Asset
    {
        public int Id { get; set; }

        [Required]
        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        [Required, MaxLength(50)]
        public string AssetCode { get; set; } = "";

        [Required, MaxLength(150)]
        public string AssetName { get; set; } = "";

        [MaxLength(100)]
        public string? AssetType { get; set; }

        [MaxLength(150)]
        public string? SerialNumber { get; set; }

        public string? Description { get; set; }

        public int? CurrentEmployeeId { get; set; }
        public Employee? CurrentEmployee { get; set; }

        public DateTime? AssignedOn { get; set; }
        public DateTime? ExpectedReturnDate { get; set; }
        public DateTime? ReturnedOn { get; set; }

        [MaxLength(30)]
        public string? Status { get; set; }

        [MaxLength(100)]
        public string? ConditionOnAssignment { get; set; }

        [MaxLength(100)]
        public string? ConditionOnReturn { get; set; }

        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }


    // ============================================================
    // HRM - ASSET HISTORY
    // ============================================================

    public class AssetHistory
    {
        public int Id { get; set; }

        [Required]
        public int AssetId { get; set; }
        public Asset? Asset { get; set; }

        public int? EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        [Required, MaxLength(50)]
        public string Action { get; set; } = "";

        public DateTime ActionDate { get; set; }

        [MaxLength(100)]
        public string? Condition { get; set; }

        public string? Remarks { get; set; }

        [Required]
        public int PerformedBy { get; set; }
        public User? PerformedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }


    // ============================================================
    // HRM - AUDIT LOG
    // ============================================================

    public class AuditLog
    {
        public int Id { get; set; }

        public int? CompanyId { get; set; }
        public Company? Company { get; set; }

        [Required]
        public int UserId { get; set; }
        public User? User { get; set; }

        [Required, MaxLength(100)]
        public string Action { get; set; } = "";

        [Required, MaxLength(100)]
        public string EntityName { get; set; } = "";

        public int? EntityId { get; set; }

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }


}
