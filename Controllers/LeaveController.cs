using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using System.Security.Claims;
using System.Text.Json;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Employee,HR,Management")]
    public class LeaveController : Controller
    {
        private readonly AppDbContext _db;

        // Leave policy is company-specific and stored in HRMConfiguration.
        // These values are the safe defaults used when no LeavePolicy
        // configuration has been created yet.
        private const decimal DefaultAnnualPaidLeave = 10m;
        private const decimal DefaultLongServiceAnnualPaidLeave = 15m;
        private const int DefaultLongServiceAfterYears = 5;



        public LeaveController(AppDbContext db)
        {
            _db = db;


        }
        // ============================================================
        // LEAVE POLICY
        // ============================================================

        private sealed class LeavePolicySettings
        {
            public decimal BaseAnnualPaidLeave { get; set; }
                = DefaultAnnualPaidLeave;

            public decimal LongServiceAnnualPaidLeave { get; set; }
                = DefaultLongServiceAnnualPaidLeave;

            public int LongServiceAfterYears { get; set; }
                = DefaultLongServiceAfterYears;

            public Dictionary<string, string> LeaveTypes { get; set; }
                = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["CL"] = "Casual Leave",
                    ["SL"] = "Sick Leave",
                    ["C/Off"] = "Compensatory Off",
                    ["Paternity"] = "Paternity Leave",
                    ["Maternity"] = "Maternity Leave",
                    ["Medical"] = "Medical Leave"
                };
        }

        private async Task<LeavePolicySettings> GetLeavePolicyAsync()
        {
            var now = DateTime.UtcNow;

            var configuration = await _db.HRMConfigurations
                .AsNoTracking()
                .Where(c =>
                    c.CompanyId == CurrentCompanyId &&
                    c.ConfigurationType == "LeavePolicy" &&
                    c.ConfigurationCode == "DEFAULT" &&
                    c.IsActive &&
                    c.EffectiveFrom <= now &&
                    (
                        c.EffectiveTo == null ||
                        c.EffectiveTo >= now
                    ))
                .OrderByDescending(c => c.Version)
                .FirstOrDefaultAsync();

            if (configuration == null ||
                string.IsNullOrWhiteSpace(configuration.ConfigurationJson))
            {
                return new LeavePolicySettings();
            }

            try
            {
                var policy =
                    JsonSerializer.Deserialize<LeavePolicySettings>(
                        configuration.ConfigurationJson,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                if (policy == null)
                    return new LeavePolicySettings();

                policy.LeaveTypes =
                    policy.LeaveTypes != null &&
                    policy.LeaveTypes.Count > 0
                        ? new Dictionary<string, string>(
                            policy.LeaveTypes,
                            StringComparer.OrdinalIgnoreCase)
                        : new LeavePolicySettings().LeaveTypes;

                if (policy.BaseAnnualPaidLeave <= 0)
                    policy.BaseAnnualPaidLeave = DefaultAnnualPaidLeave;

                if (policy.LongServiceAnnualPaidLeave <= 0)
                    policy.LongServiceAnnualPaidLeave =
                        DefaultLongServiceAnnualPaidLeave;

                if (policy.LongServiceAfterYears < 0)
                    policy.LongServiceAfterYears =
                        DefaultLongServiceAfterYears;

                return policy;
            }
            catch
            {
                // Invalid configuration should never break Leave management.
                return new LeavePolicySettings();
            }
        }



        private static int CalculateCompletedYears(
    DateTime? joiningDate,
    DateTime asOfDate)
        {
            if (!joiningDate.HasValue)
                return 0;

            var joined = joiningDate.Value.Date;
            var today = asOfDate.Date;

            if (joined > today)
                return 0;

            var years = today.Year - joined.Year;

            if (today < joined.AddYears(years))
                years--;

            return Math.Max(0, years);
        }

        private static Dictionary<string, string> GetAllowedLeaveTypesForEmployee(
    Employee employee,
    Dictionary<string, string> leaveTypes)
        {
            var allowedLeaveTypes = new Dictionary<string, string>(
                leaveTypes,
                StringComparer.OrdinalIgnoreCase);

            if (string.Equals(
                    employee.Gender,
                    "Male",
                    StringComparison.OrdinalIgnoreCase))
            {
                allowedLeaveTypes.Remove("Maternity");
            }
            else if (string.Equals(
                         employee.Gender,
                         "Female",
                         StringComparison.OrdinalIgnoreCase))
            {
                allowedLeaveTypes.Remove("Paternity");
            }

            return allowedLeaveTypes;
        }

        private static decimal GetAnnualLeaveEntitlement(
    Employee employee,
    LeavePolicySettings policy,
    DateTime asOfDate)
        {
            var completedYears =
                CalculateCompletedYears(
                    employee.JoiningDate,
                    asOfDate);

            return completedYears > policy.LongServiceAfterYears
                ? policy.LongServiceAnnualPaidLeave
                : policy.BaseAnnualPaidLeave;
        }
        // ============================================================
        // CURRENT USER / COMPANY
        // ============================================================

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private int CurrentCompanyId =>
            int.Parse(User.FindFirstValue("CompanyId")!);

        

        // ============================================================
        // EMPLOYEE - MY LEAVE
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> MyLeave()
        {
            var employee = await GetCurrentEmployee();

            if (employee == null)
                return Forbid();

            var currentYear = DateTime.UtcNow.AddHours(5).AddMinutes(30).Year;

            var leaves = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.EmployeeId == employee.Id &&
                    l.FromDate.Year == currentYear)
                .OrderByDescending(l => l.FromDate)
                .ToListAsync();

            var policy = await GetLeavePolicyAsync();

            var annualEntitlement =
                GetAnnualLeaveEntitlement(
                    employee,
                    policy,
                    DateTime.UtcNow.AddHours(5).AddMinutes(30).Date);

            var approvedUsed = leaves
                .Where(l => l.Status == "Approved")
                .Sum(l => l.TotalDays ?? 0m);

            var pendingDays = leaves
                .Where(l => l.Status == "Pending")
                .Sum(l => l.TotalDays ?? 0m);

            var availableBalance =
                Math.Max(
                    0m,
                    annualEntitlement - approvedUsed);

            ViewBag.Employee = employee;
            ViewBag.Leaves = leaves;
            ViewBag.LeaveTypes = policy.LeaveTypes;
            ViewBag.AnnualEntitlement = annualEntitlement;
            ViewBag.ApprovedUsed = approvedUsed;
            ViewBag.PendingDays = pendingDays;
            ViewBag.AvailableBalance = availableBalance;
            ViewBag.CurrentYear = currentYear;

            return View("~/Views/HR/MyLeave.cshtml");
        }


        // ============================================================
        // EMPLOYEE - APPLY LEAVE
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> Apply()
        {
            var employee = await GetCurrentEmployee();

            if (employee == null)
                return Forbid();

            var policy = await GetLeavePolicyAsync();

            var allowedLeaveTypes =
                GetAllowedLeaveTypesForEmployee(
                    employee,
                    policy.LeaveTypes);

            var currentDate = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30);

            var annualEntitlement =
                GetAnnualLeaveEntitlement(
                    employee,
                    policy,
                    currentDate);

            ViewBag.LeaveTypes = allowedLeaveTypes;
            ViewBag.AnnualEntitlement = annualEntitlement;

            return View("~/Views/HR/ApplyLeave.cshtml");
        }


        [Authorize(Roles = "Employee")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(
            string leaveType,
            DateTime fromDate,
            DateTime toDate,
            bool halfDay,
            string? reason)
        {
            var employee = await GetCurrentEmployee();

            if (employee == null)
                return Forbid();

            var policy = await GetLeavePolicyAsync();

            var allowedLeaveTypes =
                GetAllowedLeaveTypesForEmployee(
                    employee,
                    policy.LeaveTypes);

            leaveType = leaveType?.Trim().ToUpperInvariant() ?? "";

            // --------------------------------------------------------
            // Validate leave type
            // -------------------------------------------------------

            if (!allowedLeaveTypes.ContainsKey(leaveType))
            {
                TempData["Error"] =
                    "This leave type is not applicable to your gender.";

                return RedirectToAction(nameof(Apply));
            }

            // --------------------------------------------------------
            // Validate dates
            // --------------------------------------------------------

            fromDate = fromDate.Date;
            toDate = toDate.Date;

            if (toDate < fromDate)
            {
                TempData["Error"] =
                    "To Date cannot be earlier than From Date.";

                return RedirectToAction(nameof(Apply));
            }

            // --------------------------------------------------------
            // Calculate working leave days
            // --------------------------------------------------------

            var totalDays = CalculateLeaveDays(
                fromDate,
                toDate,
                halfDay);

            if (totalDays <= 0)
            {
                TempData["Error"] =
                    "The selected dates contain no working days.";

                return RedirectToAction(nameof(Apply));
            }

            // --------------------------------------------------------
            // Check overlapping leave
            // --------------------------------------------------------

            var overlapExists = await _db.Leaves
                .AnyAsync(l =>
                    l.EmployeeId == employee.Id &&
                    (l.Status == "Pending" ||
                     l.Status == "Approved") &&
                    l.FromDate.Date <= toDate &&
                    l.ToDate.Date >= fromDate);

            if (overlapExists)
            {
                TempData["Error"] =
                    "You already have a pending or approved leave covering one or more selected dates.";

                return RedirectToAction(nameof(Apply));
            }

            // --------------------------------------------------------
            // Calculate current annual balance
            // --------------------------------------------------------

            // --------------------------------------------------------
            // Calculate current annual balance
            // --------------------------------------------------------

            var currentYear = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30)
                .Year;

            var approvedUsed = await _db.Leaves
                .Where(l =>
                    l.EmployeeId == employee.Id &&
                    l.Status == "Approved" &&
                    l.FromDate.Year == currentYear)
                .SumAsync(l => l.TotalDays ?? 0m);

            var annualEntitlement =
                GetAnnualLeaveEntitlement(
                    employee,
                    policy,
                    DateTime.UtcNow
                        .AddHours(5)
                        .AddMinutes(30)
                        .Date);

            var availableBalance =
                annualEntitlement - approvedUsed;

            // --------------------------------------------------------
            // Leave balance validation
            // --------------------------------------------------------

            if (totalDays > availableBalance)
            {
                TempData["Error"] =
                    $"Insufficient leave balance. Available balance: {availableBalance:0.##} days.";

                return RedirectToAction(nameof(Apply));
            }





            // --------------------------------------------------------
            // Create leave application
            // --------------------------------------------------------

            var leave = new Leave
            {
                EmployeeId = employee.Id,
                LeaveType = leaveType,
                FromDate = fromDate,
                ToDate = toDate,
                TotalDays = totalDays,
                Reason = reason?.Trim(),
                LeaveBalanceBefore = Math.Max(0m, availableBalance),
                LeaveBalanceAfter =
                Math.Max(0m, availableBalance - totalDays),
                Status = "Pending",
                AppliedOn = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Leaves.Add(leave);

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Leave application submitted successfully.";

            return RedirectToAction(nameof(MyLeave));
        }


        // ============================================================
        // EMPLOYEE - CANCEL LEAVE
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            var employee = await GetCurrentEmployee();

            if (employee == null)
                return Forbid();

            var policy = await GetLeavePolicyAsync();

            var allowedLeaveTypes =
                GetAllowedLeaveTypesForEmployee(
                    employee,
                    policy.LeaveTypes);

            var leave = await _db.Leaves
                .FirstOrDefaultAsync(l =>
                    l.Id == id &&
                    l.EmployeeId == employee.Id);

            if (leave == null)
                return NotFound();

            if (leave.Status != "Approved" &&
                leave.Status != "Pending")
            {
                TempData["Error"] =
                    "This leave cannot be cancelled.";

                return RedirectToAction(nameof(MyLeave));
            }

            leave.Status = "Cancelled";
            leave.CancelledOn = DateTime.UtcNow;
            leave.UpdatedAt = DateTime.UtcNow;

            // If today's approved leave created a system attendance
            // record, make the day available for attendance again.
            var todayIndia = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30)
                .Date;

            if (leave.FromDate.Date <= todayIndia &&
                leave.ToDate.Date >= todayIndia)
            {
                var attendanceDate = new DateTime(
                    todayIndia.Year,
                    todayIndia.Month,
                    todayIndia.Day,
                    0,
                    0,
                    0,
                    DateTimeKind.Unspecified)
                    .AddHours(-5)
                    .AddMinutes(-30);

                var attendance = await _db.Attendances
                    .FirstOrDefaultAsync(a =>
                        a.EmployeeId == employee.Id &&
                        a.AttendanceDate == attendanceDate &&
                        a.Source == "System" &&
                        a.Status == "On Leave" &&
                        a.CheckIn == null &&
                        a.CheckOut == null);

                if (attendance != null)
                {
                    attendance.Status = "No Punch-In";
                    attendance.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Leave cancelled successfully.";

            return RedirectToAction(nameof(MyLeave));
        }


        // ============================================================
        // HR - LEAVE MANAGEMENT
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> Index(
            string? status,
            string? leaveType,
            int? employeeId)
        {
            var leavesQuery = _db.Leaves
                .AsNoTracking()
                .Include(l => l.Employee)
                    .ThenInclude(e => e!.User)
                .Where(l =>
                    l.Employee!.User.CompanyId == CurrentCompanyId);

            if (!string.IsNullOrWhiteSpace(status))
            {
                leavesQuery = leavesQuery
                    .Where(l => l.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(leaveType))
            {
                leaveType = leaveType.Trim().ToUpperInvariant();

                leavesQuery = leavesQuery
                    .Where(l => l.LeaveType == leaveType);
            }

            if (employeeId.HasValue)
            {
                leavesQuery = leavesQuery
                    .Where(l => l.EmployeeId == employeeId.Value);
            }

            var leaves = await leavesQuery
                .OrderByDescending(l => l.AppliedOn)
                .ToListAsync();

            var employees = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee)
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            var policy = await GetLeavePolicyAsync();

            ViewBag.Status = status;
            ViewBag.LeaveType = leaveType;
            ViewBag.EmployeeId = employeeId;
            ViewBag.Employees = employees;
            ViewBag.LeaveTypes = policy.LeaveTypes;

            return View("~/Views/HR/HRLeave.cshtml", leaves);
        }


        // ============================================================
        // HR - APPROVE
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(
            int id,
            string? hrRemarks)
        {
            var leave = await _db.Leaves
                .Include(l => l.Employee)
                    .ThenInclude(e => e!.User)
                .FirstOrDefaultAsync(l =>
                    l.Id == id &&
                    l.Employee!.User.CompanyId == CurrentCompanyId);

            if (leave == null)
                return NotFound();

            if (leave.Status != "Pending")
            {
                TempData["Error"] =
                    "Only pending leave applications can be approved.";

                return RedirectToAction(nameof(Index));
            }

            var year = leave.FromDate.Year;

            var approvedUsed = await _db.Leaves
                .Where(l =>
                    l.EmployeeId == leave.EmployeeId &&
                    l.Status == "Approved" &&
                    l.Id != leave.Id &&
                    l.FromDate.Year == year)
                .SumAsync(l => l.TotalDays ?? 0m);

            var policy = await GetLeavePolicyAsync();

            var annualEntitlement =
                GetAnnualLeaveEntitlement(
                    leave.Employee!,
                    policy,
                    DateTime.UtcNow.AddHours(5).AddMinutes(30).Date);

            var availableBalance =
                annualEntitlement - approvedUsed;


            leave.LeaveBalanceBefore =
     Math.Max(0m, availableBalance);

            leave.LeaveBalanceAfter =
                Math.Max(
                    0m,
                    availableBalance - (leave.TotalDays ?? 0m));

            leave.Status = "Approved";
            leave.ReviewedBy = CurrentUserId;
            leave.ReviewedOn = DateTime.UtcNow;
            leave.HRRemarks = hrRemarks?.Trim();
            leave.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Leave approved successfully.";

            return RedirectToAction(nameof(Index));
        }


        // ============================================================
        // HR - REJECT
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(
            int id,
            string? hrRemarks)
        {
            var leave = await _db.Leaves
                .Include(l => l.Employee)
                    .ThenInclude(e => e!.User)
                .FirstOrDefaultAsync(l =>
                    l.Id == id &&
                    l.Employee!.User.CompanyId == CurrentCompanyId);

            if (leave == null)
                return NotFound();

            if (leave.Status != "Pending")
            {
                TempData["Error"] =
                    "Only pending leave applications can be rejected.";

                return RedirectToAction(nameof(Index));
            }

            leave.Status = "Rejected";
            leave.ReviewedBy = CurrentUserId;
            leave.ReviewedOn = DateTime.UtcNow;
            leave.HRRemarks = hrRemarks?.Trim();
            leave.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Leave rejected successfully.";

            return RedirectToAction(nameof(Index));
        }


        // ============================================================
        // MANAGEMENT - READ ONLY
        // ============================================================

        [Authorize(Roles = "Management")]
        [HttpGet]
        public async Task<IActionResult> Management(
            int? companyId,
            DateTime? fromDate,
            DateTime? toDate,
            string? status,
            string? leaveType)
        {
            companyId ??= 1;

            var selectedCompany = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);

            if (selectedCompany == null)
                return NotFound();

            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            var query = _db.Leaves
                .AsNoTracking()
                .Include(l => l.Employee)
                    .ThenInclude(e => e!.User)
                .Where(l =>
                    l.Employee!.User.CompanyId == companyId.Value);

            if (fromDate.HasValue)
            {
                var from = fromDate.Value.Date;

                query = query.Where(l =>
                    l.ToDate.Date >= from);
            }

            if (toDate.HasValue)
            {
                var to = toDate.Value.Date;

                query = query.Where(l =>
                    l.FromDate.Date <= to);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(l =>
                    l.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(leaveType))
            {
                leaveType = leaveType.Trim().ToUpperInvariant();

                query = query.Where(l =>
                    l.LeaveType == leaveType);
            }

            var leaves = await query
                .OrderByDescending(l => l.FromDate)
                .ToListAsync();

            ViewBag.SelectedCompanyId = companyId.Value;
            ViewBag.SelectedCompanyName = selectedCompany.Name;
            ViewBag.Companies = companies;

            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.Status = status;
            ViewBag.LeaveType = leaveType;

            return View("~/Views/HR/ManagementLeave.cshtml", leaves);
        }


        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<Employee?> GetCurrentEmployee()
        {
            return await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive");
        }


        private static decimal CalculateLeaveDays(
            DateTime fromDate,
            DateTime toDate,
            bool halfDay)
        {
            decimal workingDays = 0m;

            for (var date = fromDate.Date;
                 date <= toDate.Date;
                 date = date.AddDays(1))
            {
                if (!IsWorkingDay(date))
                    continue;

                workingDays += 1m;
            }

            if (halfDay)
            {
                // Half-day is supported as 0.5 day.
                // It applies to a single working day only.
                if (workingDays != 1m)
                    return 0m;

                return 0.5m;
            }

            return workingDays;
        }


        private static bool IsWorkingDay(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                var occurrence =
                    ((date.Day - 1) / 7) + 1;

                // 2nd and 4th Saturdays are holidays.
                if (occurrence == 2 || occurrence == 4)
                    return false;
            }

            return true;
        }
    }
}