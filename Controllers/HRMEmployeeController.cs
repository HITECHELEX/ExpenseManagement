using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExpenseManagement.Data;
using ExpenseManagement.Models;

using ExpenseManagement.Services;
namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Employee,HR,Management")]
    public class HRMEmployeeController : Controller
    {
        private readonly AppDbContext _db;

        public HRMEmployeeController(AppDbContext db)
        {
            _db = db;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private int CurrentCompanyId =>
            int.Parse(User.FindFirstValue("CompanyId")!);

        // ============================================================
        // EMPLOYEE SELF-SERVICE
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var employee = await _db.Users
                .AsNoTracking()
                .Include(u => u.Company)
                .FirstOrDefaultAsync(u =>
                    u.Id == CurrentUserId &&
                    u.Role == UserRole.Employee);

            if (employee == null)
                return Forbid();


            return View("~/Views/HR/EmployeeDashboard.cshtml", employee);
        }

        // ============================================================
        // EMPLOYEE SELF-SERVICE - MY PROFILE
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> MyProfile()
        {
            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                    .ThenInclude(u => u.Company)
                .Include(e => e.ReportingManager)
                    .ThenInclude(r => r!.User)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            return View("~/Views/HR/MyProfile.cshtml", employee);
        }

        // ============================================================
        // EMPLOYEE SELF-SERVICE - MY ATTENDANCE
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> MyAttendance(DateTime? date)
        {
            // Calendar date displayed to the employee in India.
            var selectedDate = date.HasValue
                ? date.Value.Date
                : DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            // Convert India calendar date to the same database date
            // representation used by AttendanceController.
            var attendanceDate = new DateTime(
                selectedDate.Year,
                selectedDate.Month,
                selectedDate.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified)
                .AddHours(-5)
                .AddMinutes(-30);

            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive");

            if (employee == null)
                return NotFound();

            var approvedLeave = await _db.Leaves
    .AsNoTracking()
    .FirstOrDefaultAsync(l =>
        l.EmployeeId == employee.Id &&
        l.Status == "Approved" &&
        l.FromDate <= selectedDate &&
        l.ToDate >= selectedDate &&
        l.TotalDays >= 1);

            ViewBag.ApprovedLeave = approvedLeave;

            var attendance = await _db.Attendances
    .AsNoTracking()
    .FirstOrDefaultAsync(a =>
        a.EmployeeId == employee.Id &&
        a.AttendanceDate == attendanceDate);

            // ============================================================
            // MONTHLY ATTENDANCE
            // ============================================================

            var monthStart = new DateTime(
                selectedDate.Year,
                selectedDate.Month,
                1);

            var nextMonthStart = monthStart.AddMonths(1);

            // Convert India month boundaries to the UTC representation
            // used by Attendance.AttendanceDate.
            var monthStartUtc = new DateTime(
                monthStart.Year,
                monthStart.Month,
                monthStart.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified)
                .AddHours(-5)
                .AddMinutes(-30);

            var nextMonthStartUtc = new DateTime(
                nextMonthStart.Year,
                nextMonthStart.Month,
                nextMonthStart.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified)
                .AddHours(-5)
                .AddMinutes(-30);

            var monthlyAttendance = await _db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.EmployeeId == employee.Id &&
                    a.AttendanceDate >= monthStartUtc &&
                    a.AttendanceDate < nextMonthStartUtc)
                .OrderBy(a => a.AttendanceDate)
                .ToListAsync();

            var monthlyApprovedLeaves = await _db.Leaves
    .AsNoTracking()
    .Where(l =>
        l.EmployeeId == employee.Id &&
        l.Status == "Approved" &&
        l.CancelledOn == null &&
        l.FromDate.Date < nextMonthStart &&
        l.ToDate.Date >= monthStart)
    .ToListAsync();

            // ============================================================
            // MONTHLY ATTENDANCE SUMMARY
            // ============================================================

            var monthlyPresentCount = monthlyAttendance.Count(a =>
                a.Status == "Present");

            var monthlyHalfDayCount = monthlyAttendance.Count(a =>
                a.Status == "Half Day");

            var monthlyAbsentCount = monthlyAttendance.Count(a =>
                a.Status == "Absent");

            var monthlyLeaveCount = monthlyAttendance.Count(a =>
                a.Status == "On Leave");

            var monthlyLateCount = monthlyAttendance.Count(a =>
                a.IsLate &&
                a.LateReviewStatus != "Approved");

            // ------------------------------------------------------------
            // Calculate working days for the selected month
            //
            // Monday-Friday = Working
            // 1st/3rd/5th Saturday = Working
            // 2nd/4th Saturday = Weekly Off
            // Sunday = Weekly Off
            // ------------------------------------------------------------

            var holidays = await WorkCalendar.LoadHolidaysAsync(
                _db, CurrentCompanyId, monthStart, nextMonthStart.AddDays(-1));

            var monthlyWorkingDays = 0;
            for (var day = monthStart;
                 day < nextMonthStart;
                 day = day.AddDays(1))
            {
                // Not employed on this day.
                if (employee.JoiningDate.HasValue &&
                    day.Date < employee.JoiningDate.Value.Date)
                {
                    continue;
                }

                if (employee.ExitDate.HasValue &&
                    day.Date > employee.ExitDate.Value.Date)
                {
                    continue;
                }

                if (!WorkCalendar.IsNonWorkingDay(day, holidays))
                {
                    monthlyWorkingDays++;
                }
            }

            ViewBag.MonthlyAttendance = monthlyAttendance;
            ViewBag.MonthStart = monthStart;
            ViewBag.MonthlyApprovedLeaves = monthlyApprovedLeaves;

            ViewBag.MonthlyPresentCount = monthlyPresentCount;
            ViewBag.MonthlyHalfDayCount = monthlyHalfDayCount;
            ViewBag.MonthlyAbsentCount = monthlyAbsentCount;
            ViewBag.MonthlyLeaveCount = monthlyLeaveCount;
            ViewBag.MonthlyLateCount = monthlyLateCount;
            ViewBag.MonthlyWorkingDays = monthlyWorkingDays;

            // ============================================================
            // Employee Late Attendance History
            // ============================================================

            var lateAttendanceHistory = await _db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.EmployeeId == employee.Id &&
                    a.AttendanceMode == "InOffice" &&
                    a.LateMinutes.HasValue &&
                    a.LateMinutes.Value > 0)
                .OrderByDescending(a => a.AttendanceDate)
                .ToListAsync();


            // ============================================================
            // Current Valid Late Count
            //
            // Approved late reviews are excluded.
            // ============================================================

            var currentValidLateCount = lateAttendanceHistory
                .Count(a =>
                    a.IsLate &&
                    a.LateReviewStatus != "Approved");


            // ============================================================
            // Send late information to the existing view
            // ============================================================

            ViewBag.LateAttendanceHistory =
                lateAttendanceHistory;

            ViewBag.CurrentValidLateCount =
                currentValidLateCount;

            ViewBag.AttendanceDate = selectedDate;

            ViewBag.IsToday =
                selectedDate == DateTime.UtcNow
                    .AddHours(5)
                    .AddMinutes(30)
                    .Date;

            return View(
                "~/Views/HR/MyAttendance.cshtml",
                attendance);
        }

        // ============================================================
        // EMPLOYEE SELF-SERVICE - MY PAYROLL
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> MyPayroll(
            int? month,
            int? year)
        {
            var todayIndia = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30)
                .Date;

            var selectedMonth = month ?? todayIndia.Month;
            var selectedYear = year ?? todayIndia.Year;

            if (selectedMonth < 1 || selectedMonth > 12)
                selectedMonth = todayIndia.Month;

            if (selectedYear < 2000 || selectedYear > 2100)
                selectedYear = todayIndia.Year;

            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                    .ThenInclude(u => u.Company)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return Forbid();

            var payrolls = await _db.Payrolls
                .AsNoTracking()
                .Where(p =>
                    p.EmployeeId == employee.Id &&
                    p.Employee.User.CompanyId == CurrentCompanyId &&
                    (p.Status == "Processed" ||
                     p.Status == "Finalized"))
                .OrderByDescending(p => p.Year)
                .ThenByDescending(p => p.Month)
                .ToListAsync();

            var selectedPayroll = payrolls
                .FirstOrDefault(p =>
                    p.Month == selectedMonth &&
                    p.Year == selectedYear);

            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;

            ViewBag.SelectedPayroll = selectedPayroll;

            return View(
    "~/Views/Payroll/MyPayroll.cshtml",
    payrolls);
        }

        // ============================================================
        // EMPLOYEE SELF-SERVICE - OFFICIAL PAYSLIP
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> Payslip(
            int month,
            int year)
        {
            // ---------------------------------------------------------
            // VALIDATE MONTH / YEAR
            // ---------------------------------------------------------

            if (month < 1 || month > 12 ||
                year < 2000 || year > 2100)
            {
                TempData["Error"] =
                    "Invalid payroll period.";

                return RedirectToAction(
                    nameof(MyPayroll),
                    new
                    {
                        month,
                        year
                    });
            }


            // ---------------------------------------------------------
            // FIND CURRENT EMPLOYEE
            // ---------------------------------------------------------

            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                    .ThenInclude(u => u.Company)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return Forbid();


            // ---------------------------------------------------------
            // FIND PAYROLL
            // ---------------------------------------------------------

            var payroll = await _db.Payrolls
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.EmployeeId == employee.Id &&
                    p.Month == month &&
                    p.Year == year);

            if (payroll == null)
            {
                TempData["Error"] =
                    "Payroll record was not found.";

                return RedirectToAction(
                    nameof(MyPayroll),
                    new
                    {
                        month,
                        year
                    });
            }


            // ---------------------------------------------------------
            // OFFICIAL PAYSLIP ONLY AFTER FINALIZATION
            // ---------------------------------------------------------

            if (payroll.Status != "Finalized")
            {
                TempData["Error"] =
                    "Salary slip can only be accessed after payroll has been finalized.";

                return RedirectToAction(
                    nameof(MyPayroll),
                    new
                    {
                        month,
                        year
                    });
            }


            // ---------------------------------------------------------
            // FIND SALARY STRUCTURE APPLICABLE TO PAYROLL MONTH
            // ---------------------------------------------------------

            var monthStart =
                new DateTime(year, month, 1);

            var nextMonthStart =
                monthStart.AddMonths(1);

            var salary = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s =>
                    s.EmployeeId == employee.Id &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .FirstOrDefaultAsync();


            // ---------------------------------------------------------
            // USE THE SAME OFFICIAL PAYSLIP VIEW USED BY HR
            // ---------------------------------------------------------

            ViewBag.Employee = employee;
            ViewBag.Payroll = payroll;
            ViewBag.Salary = salary;

            ViewBag.Month = month;
            ViewBag.Year = year;

            return View(
                "~/Views/Payroll/Payslip.cshtml");
        }

        // ============================================================
        // EMPLOYEE MANAGEMENT
        // HR + MANAGEMENT
        // ============================================================

        [Authorize(Roles = "HR,Management")]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var employees = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Include(e => e.ReportingManager)
                    .ThenInclude(r => r!.User)
                .Where(e =>
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee)
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            return View("~/Views/HR/EmployeeView.cshtml", employees);
        }

        // ============================================================
        // CREATE EMPLOYEE - GET
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await LoadCreateEmployeeData();

            return View("~/Views/HR/CreateEmployee.cshtml");
        }

        // ============================================================
        // CREATE EMPLOYEE - POST
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee model)
        {
            var isNewEmployee = string.Equals(
                Request.Form["EmployeeMode"],
                "new",
                StringComparison.OrdinalIgnoreCase);

            User? user = null;

            model.EmploymentStatus = "Active";

            // ========================================================
            // NEW EMPLOYEE MODE
            // ========================================================

            if (isNewEmployee)
            {
                var newFullName =
                    Request.Form["NewFullName"].ToString().Trim();

                var newEmail =
                    Request.Form["NewEmail"].ToString().Trim();

                var newPassword =
                    Request.Form["NewPassword"].ToString();

                var newDepartment =
                    Request.Form["NewDepartment"].ToString().Trim();

                if (string.IsNullOrWhiteSpace(newFullName))
                {
                    ModelState.AddModelError(
                        "NewFullName",
                        "Full name is required.");
                }

                if (string.IsNullOrWhiteSpace(newEmail))
                {
                    ModelState.AddModelError(
                        "NewEmail",
                        "Email is required.");
                }

                if (string.IsNullOrWhiteSpace(newPassword))
                {
                    ModelState.AddModelError(
                        "NewPassword",
                        "Password is required.");
                }

                if (newPassword.Length > 0 &&
                    newPassword.Length < 6)
                {
                    ModelState.AddModelError(
                        "NewPassword",
                        "Password must be at least 6 characters.");
                }

                if (!string.IsNullOrWhiteSpace(newEmail) &&
                    await _db.Users.AnyAsync(u => u.Email == newEmail))
                {
                    ModelState.AddModelError(
                        "NewEmail",
                        "Email already exists.");
                }

                // UserId will be generated when the User is created.
                model.UserId = 0;

                // Remove required validation for UserId.
                ModelState.Remove(nameof(Employee.UserId));
            }
            else
            {
                // ========================================================
                // EXISTING EMPLOYEE USER MODE
                // ========================================================

                user = await _db.Users
                    .FirstOrDefaultAsync(u =>
                        u.Id == model.UserId &&
                        u.CompanyId == CurrentCompanyId &&
                        u.Role == UserRole.Employee);

                if (user == null)
                {
                    ModelState.AddModelError(
                        "UserId",
                        "Invalid employee user selected.");
                }
                else if (await _db.Employees.AnyAsync(
                    e => e.UserId == model.UserId))
                {
                    ModelState.AddModelError(
                        "UserId",
                        "An HR employee profile already exists for this user.");
                }
            }

            // ========================================================
            // COMMON VALIDATION
            // ========================================================

            if (await _db.Employees.AnyAsync(
                e => e.EmployeeCode == model.EmployeeCode))
            {
                ModelState.AddModelError(
                    "EmployeeCode",
                    "This employee code is already in use.");
            }

            if (model.ReportingManagerId.HasValue)
            {
                var validManager = await _db.Employees
                    .Include(e => e.User)
                    .AnyAsync(e =>
                        e.Id == model.ReportingManagerId.Value &&
                        e.User.CompanyId == CurrentCompanyId &&
                        e.User.Role == UserRole.Employee &&
                        e.EmploymentStatus != "Inactive");

                if (!validManager)
                {
                    ModelState.AddModelError(
                        "ReportingManagerId",
                        "Invalid reporting manager selected.");
                }
            }

            // ========================================================
            // VALIDATION FAILURE
            // ========================================================

            if (!ModelState.IsValid)
            {
                foreach (var entry in ModelState)
                {
                    foreach (var error in entry.Value.Errors)
                    {
                        Console.WriteLine(
                            $"HRM CREATE VALIDATION ERROR: " +
                            $"{entry.Key} - {error.ErrorMessage}");
                    }
                }

                ViewBag.EmployeeMode = isNewEmployee ? "new" : "existing";
                await LoadCreateEmployeeData(model.UserId);

                return View(
                    "~/Views/HR/CreateEmployee.cshtml",
                    model);
            }

            // ========================================================
            // DATABASE TRANSACTION
            // ========================================================

            await using var transaction =
                await _db.Database.BeginTransactionAsync();

            try
            {
                // ====================================================
                // CREATE USER FOR NEW EMPLOYEE
                // ====================================================

                if (isNewEmployee)
                {
                    var newFullName =
                        Request.Form["NewFullName"].ToString().Trim();

                    var newEmail =
                        Request.Form["NewEmail"].ToString().Trim();

                    var newPassword =
                        Request.Form["NewPassword"].ToString();

                    var newDepartment =
                        Request.Form["NewDepartment"].ToString().Trim();

                    user = new User
                    {
                        FullName = newFullName,
                        Email = newEmail,
                        PasswordHash =
                            BCrypt.Net.BCrypt.HashPassword(newPassword),
                        Role = UserRole.Employee,
                        Department =
                            string.IsNullOrWhiteSpace(newDepartment)
                                ? null
                                : newDepartment,
                        CompanyId = CurrentCompanyId,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.Users.Add(user);

                    // Save User first to obtain generated User.Id.
                    await _db.SaveChangesAsync();

                    model.UserId = user.Id;
                }

                // ====================================================
                // CREATE HR EMPLOYEE PROFILE
                // ====================================================

                model.CreatedAt = DateTime.UtcNow;
                model.UpdatedAt = DateTime.UtcNow;

                _db.Employees.Add(model);
                await _db.SaveChangesAsync();

                WriteAudit("Create", model.Id,
                    $"Created employee profile {model.EmployeeCode} for user {model.UserId}.");
                await _db.SaveChangesAsync();

                // ====================================================
                // COMMIT
                // ====================================================

                await transaction.CommitAsync();

                TempData["Success"] =
                    $"Employee '{user!.FullName}' created successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                Console.WriteLine(
                    "HRM CREATE EMPLOYEE ERROR:");

                Console.WriteLine(ex.ToString());

                ModelState.AddModelError(
                    "",
                    "Unable to create the employee. No changes were saved.");

                await LoadCreateEmployeeData(model.UserId);
                ViewBag.EmployeeMode = isNewEmployee ? "new" : "existing";
                return View(
                    "~/Views/HR/CreateEmployee.cshtml",
                    model);
            }
        }

        // ============================================================
        // DETAILS
        // ============================================================

        [Authorize(Roles = "HR,Management")]
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                    .ThenInclude(u => u.Company)
                .Include(e => e.ReportingManager)
                    .ThenInclude(r => r!.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == id &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            return View(
                "~/Views/HR/EmployeeDetails.cshtml",
                employee);
        }

        // ============================================================
        // EDIT - GET
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == id &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            var managers = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive" &&
                    e.Id != id)
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            ViewBag.Managers = managers;

            return View(
                "~/Views/HR/EditEmployee.cshtml",
                employee);
        }

        // ============================================================
        // EDIT - POST
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            Employee model)
        {
            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == id &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            if (await _db.Employees.AnyAsync(e =>
                    e.EmployeeCode == model.EmployeeCode &&
                    e.Id != id))
            {
                ModelState.AddModelError(
                    "EmployeeCode",
                    "This employee code is already in use.");
            }

            if (model.ReportingManagerId.HasValue)
            {
                if (model.ReportingManagerId.Value == id)
                {
                    ModelState.AddModelError(
                        "ReportingManagerId",
                        "An employee cannot report to themselves.");
                }
                else
                {
                    var validManager = await _db.Employees
                        .Include(e => e.User)
                        .AnyAsync(e =>
                            e.Id == model.ReportingManagerId.Value &&
                            e.User.CompanyId == CurrentCompanyId &&
                            e.User.Role == UserRole.Employee);

                    if (!validManager)
                    {
                        ModelState.AddModelError(
                            "ReportingManagerId",
                            "Invalid reporting manager selected.");
                    }
                }
            }

            // ========================================================
            // LIFECYCLE VALIDATIONS
            // ========================================================
            var allowedStatuses = new[] { "Active", "On Notice", "Inactive" };
            var todayIst = DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            if (string.IsNullOrWhiteSpace(model.EmploymentStatus) ||
                !allowedStatuses.Contains(model.EmploymentStatus))
            {
                ModelState.AddModelError(
                    "",
                    "Select a valid employment status.");
            }

            if (model.ExitDate.HasValue &&
                model.JoiningDate.HasValue &&
                model.ExitDate.Value.Date < model.JoiningDate.Value.Date)
            {
                ModelState.AddModelError(
                    "",
                    "Exit date cannot be earlier than the joining date.");
            }

            if (model.EmploymentStatus == "Inactive" && !model.ExitDate.HasValue)
            {
                ModelState.AddModelError(
                    "",
                    "Exit date is required when the employee is Inactive.");
            }

            if (model.EmploymentStatus != "Inactive" &&
                model.ExitDate.HasValue &&
                model.ExitDate.Value.Date < todayIst)
            {
                ModelState.AddModelError(
                    "",
                    "The exit date is in the past. Set the status to Inactive or clear the exit date.");
            }

            if (model.ReportingManagerId.HasValue &&
                model.ReportingManagerId.Value != id)
            {
                // The new manager must be active (only checked when changed).
                if (model.ReportingManagerId != employee.ReportingManagerId)
                {
                    var managerIsActive = await _db.Employees.AnyAsync(e =>
                        e.Id == model.ReportingManagerId.Value &&
                        e.EmploymentStatus != "Inactive");

                    if (!managerIsActive)
                    {
                        ModelState.AddModelError(
                    "",
                            "The selected reporting manager is not active.");
                    }
                }

                // Block circular reporting lines (A -> B -> A).
                var reportingLines = await _db.Employees
                    .AsNoTracking()
                    .Where(e => e.User.CompanyId == CurrentCompanyId)
                    .Select(e => new { e.Id, e.ReportingManagerId })
                    .ToDictionaryAsync(e => e.Id, e => e.ReportingManagerId);

                var cursor = model.ReportingManagerId;
                var steps = 0;

                while (cursor.HasValue && steps++ < 100)
                {
                    if (cursor.Value == id)
                    {
                        ModelState.AddModelError(
                    "",
                            "This selection creates a circular reporting line.");
                        break;
                    }

                    cursor = reportingLines.TryGetValue(cursor.Value, out var nextManager)
                        ? nextManager
                        : null;
                }
            }

            if (!ModelState.IsValid)
            {
                var managers = await _db.Employees
                    .AsNoTracking()
                    .Include(e => e.User)
                    .Where(e =>
                        e.User.CompanyId == CurrentCompanyId &&
                        e.User.Role == UserRole.Employee &&
                        e.EmploymentStatus != "Inactive" &&
                        e.Id != id)
                    .OrderBy(e => e.User.FullName)
                    .ToListAsync();

                ViewBag.Managers = managers;

                return View(
                    "~/Views/HR/EditEmployee.cshtml",
                    model);
            }

            // Track key changes for the audit trail (no personal/bank data).
            var previousStatus = employee.EmploymentStatus;
            var changes = new List<string>();

            void Track(string label, object? before, object? after)
            {
                var b = before?.ToString() ?? "";
                var a = after?.ToString() ?? "";
                if (b != a) changes.Add($"{label}: '{b}' -> '{a}'");
            }

            Track("Employee code", employee.EmployeeCode, model.EmployeeCode);
            Track("Designation", employee.Designation, model.Designation);
            Track("Reporting manager id", employee.ReportingManagerId, model.ReportingManagerId);
            Track("Status", employee.EmploymentStatus, model.EmploymentStatus);
            Track("Employment type", employee.EmploymentType, model.EmploymentType);
            Track("Joining date", employee.JoiningDate?.ToString("yyyy-MM-dd"), model.JoiningDate?.ToString("yyyy-MM-dd"));
            Track("Exit date", employee.ExitDate?.ToString("yyyy-MM-dd"), model.ExitDate?.ToString("yyyy-MM-dd"));

            // Update HR profile fields only.
            // Update HR profile fields only.
            employee.EmployeeCode = model.EmployeeCode;
            employee.DateOfBirth = model.DateOfBirth;
            employee.Gender = model.Gender;
            employee.Phone = model.Phone;
            employee.AlternatePhone = model.AlternatePhone;
            employee.EmergencyContactPhone =
                model.EmergencyContactPhone;
            employee.PersonalEmail = model.PersonalEmail;
            employee.PresentAddress = model.PresentAddress;
            employee.AadhaarAddress = model.AadhaarAddress;
            employee.City = model.City;
            employee.State = model.State;
            employee.Pincode = model.Pincode;
            employee.EmergencyContactName =
                model.EmergencyContactName;
            employee.JoiningDate = model.JoiningDate;
            employee.Designation = model.Designation;
            employee.ReportingManagerId =
                model.ReportingManagerId;
            employee.EmploymentStatus =
                model.EmploymentStatus;
            employee.EmploymentType =
                model.EmploymentType;
            employee.ExitDate = model.ExitDate;
            employee.ExitReason = model.ExitReason;

            // Bank Details
            employee.BankName = model.BankName;
            employee.BankBranch = model.BankBranch;
            employee.BankAccountNumber = model.BankAccountNumber;
            employee.BankIFSCCode = model.BankIFSCCode;
            employee.BankAccountHolderName = model.BankAccountHolderName;

            // Identity Information
            employee.AadhaarNumber = model.AadhaarNumber;
            employee.PANNumber = model.PANNumber;

            // Medical Information
            employee.MedicalHistory = model.MedicalHistory;

            // Employee Category
            employee.IsIDBI = model.IsIDBI;
            employee.IsNonIDBI = model.IsNonIDBI;
            employee.IsOnSite = model.IsOnSite;

            // Employee Category
            employee.IsIDBI = model.IsIDBI;
            employee.IsNonIDBI = model.IsNonIDBI;

            // ========================================================
            // EXIT HANDLING (status changed to Inactive)
            // ========================================================
            var exitNote = "";

            if (previousStatus != "Inactive" &&
                employee.EmploymentStatus == "Inactive" &&
                employee.ExitDate.HasValue)
            {
                var exitDate = employee.ExitDate.Value.Date;
                var now = DateTime.UtcNow;

                // Pending leave, and approved leave starting after the exit date.
                var leavesToCancel = await _db.Leaves
                    .Where(l =>
                        l.EmployeeId == employee.Id &&
                        l.CancelledOn == null &&
                        (l.Status == "Pending" ||
                         (l.Status == "Approved" && l.FromDate.Date > exitDate)))
                    .ToListAsync();

                foreach (var leave in leavesToCancel)
                {
                    leave.Status = "Cancelled";
                    leave.CancelledOn = now;
                    leave.UpdatedAt = now;
                }

                // Direct reports move to the leaving employee's own manager.
                var directReports = await _db.Employees
                    .Where(e => e.ReportingManagerId == employee.Id)
                    .ToListAsync();

                foreach (var report in directReports)
                {
                    report.ReportingManagerId = employee.ReportingManagerId;
                    report.UpdatedAt = now;
                }

                changes.Add(
                    $"Exit processing: {leavesToCancel.Count} leave application(s) cancelled, " +
                    $"{directReports.Count} direct report(s) reassigned");

                exitNote =
                    $" Leave cancelled: {leavesToCancel.Count}. " +
                    $"Direct reports reassigned: {directReports.Count}.";
            }

            if (changes.Count > 0)
            {
                WriteAudit("Update", employee.Id,
                    $"Updated employee {employee.User.FullName}: {string.Join("; ", changes)}");
            }

            await _db.SaveChangesAsync();

            TempData["Success"] =
                $"Employee profile for " +
                $"{employee.User.FullName} updated successfully." +
                exitNote;

            return RedirectToAction(
                nameof(Details),
                new { id = employee.Id });
        }

        private void WriteAudit(string action, int? entityId, string description)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = CurrentCompanyId,
                UserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0"),
                Action = action,
                EntityName = "Employee",
                EntityId = entityId,
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }

        // ============================================================
        // CREATE VIEW DATA
        // ============================================================

        private async Task LoadCreateEmployeeData(
            int? selectedUserId = null)
        {
            var availableUsers = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.CompanyId == CurrentCompanyId &&
                    u.Role == UserRole.Employee &&
                    (u.EmployeeProfile == null ||
                     u.Id == selectedUserId))
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var managers = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive")
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            ViewBag.AvailableUsers = availableUsers;
            ViewBag.Managers = managers;
        }

        // ============================================================
        // EMPLOYEE - MY ASSETS
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> MyAssets(int? assetId)
        {
            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            // IMPORTANT:
            // Employee can ONLY see assets currently assigned to themselves.
            var assets = await _db.Assets
                .AsNoTracking()
                .Include(a => a.Company)
                .Where(a =>
                    a.CompanyId == CurrentCompanyId &&
                    a.CurrentEmployeeId == employee.Id)
                .OrderBy(a => a.AssetName)
                .ThenBy(a => a.AssetCode)
                .ToListAsync();

            // No asset selected -> My Assets page.
            if (!assetId.HasValue)
            {
                ViewBag.AssetPage = "Index";

                return View(
                    "~/Views/Asset/AssetEmployee.cshtml",
                    assets);
            }

            // IMPORTANT:
            // Search ONLY inside the employee's own assets.
            var asset = assets.FirstOrDefault(
                a => a.Id == assetId.Value);

            if (asset == null)
                return NotFound();

            ViewBag.AssetPage = "Details";
            ViewBag.Asset = asset;

            ViewBag.History =
                await _db.AssetHistories
                    .AsNoTracking()
                    .Where(h =>
                        h.AssetId == asset.Id &&
                        h.EmployeeId == employee.Id)
                    .OrderByDescending(h => h.ActionDate)
                    .ThenByDescending(h => h.Id)
                    .ToListAsync();

            return View(
                "~/Views/Asset/AssetEmployee.cshtml",
                assets);
        }
    }
}