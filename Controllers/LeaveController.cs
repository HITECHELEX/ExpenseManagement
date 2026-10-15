using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Employee,HR,Management")]
    public class LeaveController : Controller
    {
        private readonly AppDbContext _db;
        private readonly LeavePolicyService _policy;

        public LeaveController(AppDbContext db, LeavePolicyService policy)
        {
            _db = db;
            _policy = policy;
        }

        private Task<LeavePolicySettings> GetLeavePolicyAsync()
            => _policy.GetAsync(CurrentCompanyId);

        private static decimal GetAnnualLeaveEntitlement(
            Employee employee, LeavePolicySettings policy, DateTime asOfDate)
            => LeavePolicyService.GetAnnualEntitlement(employee.JoiningDate, policy, asOfDate);

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
                .Where(l => l.Status == "Approved" && policy.GetRule(l.LeaveType).UsesAnnualBalance)
                .Sum(l => l.TotalDays ?? 0m);

            var pendingDays = leaves
                .Where(l => l.Status == "Pending" && policy.GetRule(l.LeaveType).UsesAnnualBalance)
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

            if (fromDate.Year != toDate.Year)
            {
                TempData["Error"] =
                    "A leave application cannot span two calendar years. Please apply separately for each year.";

                return RedirectToAction(nameof(Apply));
            }

            if (employee.JoiningDate.HasValue &&
                fromDate < employee.JoiningDate.Value.Date)
            {
                TempData["Error"] =
                    "Leave cannot start before your joining date.";

                return RedirectToAction(nameof(Apply));
            }

            // Past dates: employees may backdate only within the current month.
            var backdateToday = DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            if (fromDate < new DateTime(backdateToday.Year, backdateToday.Month, 1))
            {
                TempData["Error"] =
                    "You can apply for past dates only within the current month. Please contact HR for earlier dates.";

                return RedirectToAction(nameof(Apply));
            }

            if (employee.ExitDate.HasValue &&
                toDate > employee.ExitDate.Value.Date)
            {
                TempData["Error"] =
                    "Leave cannot extend beyond your exit date.";

                return RedirectToAction(nameof(Apply));
            }

            // --------------------------------------------------------
            // Calculate working leave days
            // --------------------------------------------------------

            var holidays = await WorkCalendar.LoadHolidaysAsync(_db, CurrentCompanyId, fromDate, toDate);
            var totalDays = CalculateLeaveDays(fromDate, toDate, halfDay, holidays);

            if (totalDays <= 0)
            {
                TempData["Error"] =
                    "The selected dates contain no working days.";

                return RedirectToAction(nameof(Apply));
            }

            // --------------------------------------------------------
            // Serialise concurrent applications for the same employee, so
            // the overlap check below cannot be bypassed by a double-click
            // or two simultaneous requests.
            // --------------------------------------------------------

            await using var tx = await _db.Database.BeginTransactionAsync();

            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(7001, {employee.Id})");

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
            // Paid-leave balance (informational).
            // Applying beyond the balance is allowed: the excess days
            // are unpaid and are deducted in payroll.
            // --------------------------------------------------------

            var leaveYear = fromDate.Year;
            var rule = policy.GetRule(leaveType);

            var poolUsed = await GetPoolUsedAsync(
                employee.Id,
                leaveYear,
                policy,
                includePending: true,
                excludeLeaveId: null);

            var entitlement = EntitlementForYear(employee, policy, leaveYear);
            var balance = CalculateBalance(totalDays, entitlement, poolUsed, rule);

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
                LeaveBalanceBefore = balance.Before,
                LeaveBalanceAfter = balance.After,
                Status = "Pending",
                AppliedOn = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Leaves.Add(leave);

            try
            {
                await _db.SaveChangesAsync();

                WriteAudit("Apply", leave.Id, $"Applied for {leave.LeaveType} leave {leave.FromDate:dd-MMM-yyyy} to {leave.ToDate:dd-MMM-yyyy} ({leave.TotalDays:0.##} day(s)) for employee {leave.EmployeeId}.");
                await _db.SaveChangesAsync();

                await tx.CommitAsync();
            }
            catch (DbUpdateException)
            {
                TempData["Error"] =
                    "You already have a pending or approved leave covering one or more selected dates.";

                return RedirectToAction(nameof(Apply));
            }

            TempData["Success"] = balance.UnpaidDays > 0
                ? $"Leave application submitted. {balance.UnpaidDays:0.##} day(s) exceed your paid leave balance and will be unpaid (salary deducted)."
                : "Leave application submitted successfully.";

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

            // An employee can cancel only before, or on, the first leave day.
            var cancelToday = DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            if (leave.FromDate.Date < cancelToday)
            {
                TempData["Error"] =
                    "This leave has already started, so it can no longer be cancelled. Please contact HR.";

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

            WriteAudit("Cancel", leave.Id, $"Cancelled {leave.LeaveType} leave {leave.FromDate:dd-MMM-yyyy} to {leave.ToDate:dd-MMM-yyyy} ({leave.TotalDays:0.##} day(s)) for employee {leave.EmployeeId}.");

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

            var policy = await GetLeavePolicyAsync();
            var leaveYear = leave.FromDate.Year;
            var rule = policy.GetRule(leave.LeaveType);

            // Re-check overlap against leave that has meanwhile been approved.
            var leaveFrom = leave.FromDate.Date;
            var leaveTo = leave.ToDate.Date;

            var approvedOverlap = await _db.Leaves
                .AnyAsync(l =>
                    l.EmployeeId == leave.EmployeeId &&
                    l.Id != leave.Id &&
                    l.Status == "Approved" &&
                    l.FromDate.Date <= leaveTo &&
                    l.ToDate.Date >= leaveFrom);

            if (approvedOverlap)
            {
                TempData["Error"] =
                    "Cannot approve. The employee already has approved leave covering one or more of these dates.";

                return RedirectToAction(nameof(Index));
            }

            // Balance as of now (approved leave only). Days beyond the paid
            // balance are still approved but will be unpaid in payroll.
            var poolUsed = await GetPoolUsedAsync(
                leave.EmployeeId,
                leaveYear,
                policy,
                includePending: false,
                excludeLeaveId: leave.Id);

            var entitlement = EntitlementForYear(leave.Employee!, policy, leaveYear);
            var balance = CalculateBalance(leave.TotalDays ?? 0m, entitlement, poolUsed, rule);

            leave.LeaveBalanceBefore = balance.Before;
            leave.LeaveBalanceAfter = balance.After;

            leave.Status = "Approved";
            leave.ReviewedBy = CurrentUserId;
            leave.ReviewedOn = DateTime.UtcNow;
            leave.HRRemarks = hrRemarks?.Trim();
            leave.UpdatedAt = DateTime.UtcNow;

            WriteAudit("Approve", leave.Id, $"Approved {leave.LeaveType} leave {leave.FromDate:dd-MMM-yyyy} to {leave.ToDate:dd-MMM-yyyy} ({leave.TotalDays:0.##} day(s)) for employee {leave.EmployeeId}.");

            await _db.SaveChangesAsync();

            TempData["Success"] = balance.UnpaidDays > 0
                ? $"Leave approved. {balance.UnpaidDays:0.##} day(s) exceed the paid leave balance and will be unpaid (salary deducted)."
                : "Leave approved successfully.";

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

            WriteAudit("Reject", leave.Id, $"Rejected {leave.LeaveType} leave {leave.FromDate:dd-MMM-yyyy} to {leave.ToDate:dd-MMM-yyyy} ({leave.TotalDays:0.##} day(s)) for employee {leave.EmployeeId}.");

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


        private void WriteAudit(string action, int? entityId, string description)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = CurrentCompanyId,
                UserId = CurrentUserId,
                Action = action,
                EntityName = "Leave",
                EntityId = entityId,
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }

        // ============================================================
        // LEAVE ALLOWANCE HELPERS
        // ============================================================

        private decimal EntitlementForYear(
            Employee employee,
            LeavePolicySettings policy,
            int leaveYear)
        {
            var today = DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            var asOf = leaveYear > today.Year
                ? new DateTime(leaveYear, 1, 1)
                : today;

            return GetAnnualLeaveEntitlement(employee, policy, asOf);
        }

        private async Task<decimal> GetPoolUsedAsync(
            int employeeId,
            int year,
            LeavePolicySettings policy,
            bool includePending,
            int? excludeLeaveId)
        {
            var statuses = includePending
                ? new[] { "Approved", "Pending" }
                : new[] { "Approved" };

            var leaves = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.EmployeeId == employeeId &&
                    statuses.Contains(l.Status) &&
                    l.FromDate.Year == year &&
                    (excludeLeaveId == null || l.Id != excludeLeaveId))
                .Select(l => new { l.LeaveType, l.TotalDays })
                .ToListAsync();

            return leaves
                .Where(l => policy.GetRule(l.LeaveType).UsesAnnualBalance)
                .Sum(l => l.TotalDays ?? 0m);
        }

        private sealed record LeaveBalanceInfo(decimal Before, decimal After, decimal UnpaidDays);

        private static LeaveBalanceInfo CalculateBalance(
            decimal days,
            decimal entitlement,
            decimal poolUsed,
            LeaveTypeRule rule)
        {
            // Leave type outside the paid scope: every day is unpaid.
            if (!rule.IsPaid)
                return new LeaveBalanceInfo(0m, 0m, days);

            // Paid leave type that does not use the annual pool.
            if (!rule.UsesAnnualBalance)
                return new LeaveBalanceInfo(0m, 0m, 0m);

            var before = Math.Max(0m, entitlement - poolUsed);
            var after = Math.Max(0m, before - days);
            var unpaid = Math.Max(0m, days - before);

            return new LeaveBalanceInfo(before, after, unpaid);
        }

        private static decimal CalculateLeaveDays(
    DateTime fromDate, DateTime toDate, bool halfDay, ISet<DateTime> holidays)
        {
            decimal workingDays = 0m;
            for (var date = fromDate.Date; date <= toDate.Date; date = date.AddDays(1))
                if (!WorkCalendar.IsNonWorkingDay(date, holidays)) workingDays += 1m;

            if (halfDay) return workingDays == 1m ? 0.5m : 0m;
            return workingDays;
        }

    }
}