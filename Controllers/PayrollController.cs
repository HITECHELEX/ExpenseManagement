using ExpenseManagement.Data;
using ExpenseManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "HR,Management")]
    public class PayrollController : Controller
    {
        private readonly AppDbContext _db;

        public PayrollController(AppDbContext db)
        {
            _db = db;
        }

        private int CurrentCompanyId =>
            int.Parse(User.FindFirst("CompanyId")?.Value ?? "0");

        private int CurrentUserId =>
            int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        private async Task<PayrollDeductionSettings?> GetActivePayrollDeductionSettingsAsync()
        {
            var now = DateTime.UtcNow;

            var configuration = await _db.HRMConfigurations
                .AsNoTracking()
                .Where(c =>
                    c.CompanyId == CurrentCompanyId &&
                    c.ConfigurationType == "PayrollDeduction" &&
                    c.ConfigurationCode == "DEFAULT" &&
                    c.IsActive &&
                    c.EffectiveFrom <= now &&
                    (c.EffectiveTo == null || c.EffectiveTo >= now))
                .OrderByDescending(c => c.Version)
                .FirstOrDefaultAsync();

            if (configuration == null ||
                string.IsNullOrWhiteSpace(configuration.ConfigurationJson))
            {
                return null;
            }

            try
            {
                var settings =
                    JsonSerializer.Deserialize<PayrollDeductionSettings>(
                        configuration.ConfigurationJson,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                if (settings == null ||
                    !settings.PF.HasValue ||
                    !settings.ESIC.HasValue ||
                    !settings.ProfessionalTax.HasValue ||
                    !settings.LWF.HasValue)
                {
                    return null;
                }

                return settings;
            }
            catch
            {
                return null;
            }
        }

        private static DateTime GetIndiaAttendanceDate(DateTime indiaDate)
        {
            var date = new DateTime(
                indiaDate.Year,
                indiaDate.Month,
                indiaDate.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified);

            // Convert IST midnight to the UTC value
            // used by the Attendance table.
            return DateTime.SpecifyKind(
                date.AddHours(-5).AddMinutes(-30),
                DateTimeKind.Utc);
        }

        // ---------------------------------------------------------
        // PAYROLL DASHBOARD
        // ---------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Index(int? month, int? year)
        {
            var today = DateTime.UtcNow.AddHours(5).AddMinutes(30);

            var selectedMonth = month ?? today.Month;
            var selectedYear = year ?? today.Year;

            var todayIndia = DateTime.UtcNow
    .AddHours(5)
    .AddMinutes(30)
    .Date;

            if (selectedMonth < 1 || selectedMonth > 12)
                selectedMonth = today.Month;

            if (selectedYear < 2000 || selectedYear > 2100)
                selectedYear = today.Year;

            var employees = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive")
                .OrderBy(e => e.EmployeeCode)
                .ToListAsync();

            var payrolls = await _db.Payrolls
                .AsNoTracking()
                .Where(p =>
                    p.Employee.User.CompanyId == CurrentCompanyId &&
                    p.Month == selectedMonth &&
                    p.Year == selectedYear)
                .ToListAsync();

            var selectedMonthStart =
    new DateTime(selectedYear, selectedMonth, 1);

            var nextMonthStart =
                selectedMonthStart.AddMonths(1);

            var salaryMasters = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s =>
                    s.Employee.User.CompanyId == CurrentCompanyId &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .ToListAsync();

            var rows = employees.Select(employee =>
            {
                var salary = salaryMasters
                    .FirstOrDefault(s => s.EmployeeId == employee.Id);

                var payroll = payrolls
                    .FirstOrDefault(p => p.EmployeeId == employee.Id);

                return new
                {
                    Employee = employee,
                    Salary = salary,
                    Payroll = payroll
                };
            }).ToList();
            // ---------------------------------------------------------
            // PAYROLL PROCESSING WINDOW
            // ---------------------------------------------------------

            var lastDayOfSelectedMonth = new DateTime(
                selectedYear,
                selectedMonth,
                DateTime.DaysInMonth(selectedYear, selectedMonth));

            var isLastThreeDays =
                selectedYear == today.Year &&
                selectedMonth == today.Month &&
                today.Date >= lastDayOfSelectedMonth.AddDays(-2);

            ViewBag.Month = selectedMonth;
            ViewBag.Year = selectedYear;
            ViewBag.Rows = rows;

            ViewBag.IsCurrentMonth =
                selectedYear == today.Year &&
                selectedMonth == today.Month;

            ViewBag.IsPastMonth =
                selectedYear < today.Year ||
                (selectedYear == today.Year &&
                 selectedMonth < today.Month);

            ViewBag.IsFutureMonth =
                selectedYear > today.Year ||
                (selectedYear == today.Year &&
                 selectedMonth > today.Month);

            ViewBag.IsLastThreeDays = isLastThreeDays;

            return View();
        }


        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> Salary(int employeeId)
        {
            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            var salary = await _db.SalaryMasters
     .AsNoTracking()
     .FirstOrDefaultAsync(s =>
         s.EmployeeId == employee.Id &&
         s.IsActive);

            var salaryHistory = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s => s.EmployeeId == employee.Id)
                .OrderByDescending(s => s.EffectiveFrom)
                .ToListAsync();

            ViewBag.Employee = employee;
            ViewBag.Salary = salary;
            ViewBag.SalaryHistory = salaryHistory;

            return View();
        }


        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Salary(
    int employeeId,
    int? salaryId,
    string? panNumber,
    string? uanNumber,
    string? esicNumber,
    decimal basicSalary,
    decimal hra,
    decimal conveyanceAllowance,
    decimal washingAllowance,
    decimal otherAllowances,
    bool pfApplicable,
    bool esicApplicable,
    bool professionalTaxApplicable,
    bool lwfApplicable,
    DateTime effectiveFrom)
        {
            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            // Basic validation
            if (basicSalary < 0 ||
                hra < 0 ||
                conveyanceAllowance < 0 ||
                washingAllowance < 0 ||
                otherAllowances < 0)
            {
                ModelState.AddModelError(
                    "",
                    "Salary amounts cannot be negative.");
            }

            if (effectiveFrom == default)
            {
                ModelState.AddModelError(
                    "",
                    "Effective From date is required.");
            }

            if (!ModelState.IsValid)
            {
                var invalidSalary = await _db.SalaryMasters
                    .FirstOrDefaultAsync(s =>
                        s.EmployeeId == employee.Id &&
                        s.IsActive);

                ViewBag.Employee = employee;
                ViewBag.Salary = invalidSalary;

                return View();
            }

            // ---------------------------------------------------------
            // SALARY REVISION
            // ---------------------------------------------------------

            // Find the currently active salary for this employee.
            var currentSalary = await _db.SalaryMasters
                .FirstOrDefaultAsync(s =>
                    s.EmployeeId == employee.Id &&
                    s.IsActive);

            // If an existing salary is being edited/revised,
            // make sure the new effective date is not earlier
            // than the current salary's effective date.
            if (currentSalary != null &&
                effectiveFrom.Date < currentSalary.EffectiveFrom.Date)
            {
                ModelState.AddModelError(
                    "",
                    $"The new Effective From date cannot be earlier than " +
                    $"{currentSalary.EffectiveFrom:dd-MMM-yyyy}.");

                ViewBag.Employee = employee;
                ViewBag.Salary = currentSalary;

                return View();
            }

            // The existing salary is historical after a revision.
            if (currentSalary != null)
            {
                currentSalary.IsActive = false;
                currentSalary.UpdatedAt = DateTime.UtcNow;
            }

            // Always create a NEW SalaryMaster record.
            // Never overwrite the old salary structure.
            var salary = new SalaryMaster
            {
                EmployeeId = employee.Id,

                PANNumber = string.IsNullOrWhiteSpace(panNumber)
                    ? null
                    : panNumber.Trim(),

                UANNumber = string.IsNullOrWhiteSpace(uanNumber)
                    ? null
                    : uanNumber.Trim(),

                ESICNumber = string.IsNullOrWhiteSpace(esicNumber)
                    ? null
                    : esicNumber.Trim(),

                BasicSalary = basicSalary,
                HRA = hra,
                ConveyanceAllowance = conveyanceAllowance,
                WashingAllowance = washingAllowance,
                OtherAllowances = otherAllowances,

                PFApplicable = pfApplicable,
                ESICApplicable = esicApplicable,
                ProfessionalTaxApplicable =
                    professionalTaxApplicable,
                LWFApplicable = lwfApplicable,

                EffectiveFrom = DateTime.SpecifyKind(
                    effectiveFrom.Date,
                    DateTimeKind.Utc),

                IsActive = true,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.SalaryMasters.Add(salary);

            // Store the date as UTC midnight.
            salary.EffectiveFrom = DateTime.SpecifyKind(
                effectiveFrom.Date,
                DateTimeKind.Utc);

            salary.IsActive = true;
            salary.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Salary configuration saved successfully.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    month = DateTime.UtcNow.AddHours(5).AddMinutes(30).Month,
                    year = DateTime.UtcNow.AddHours(5).AddMinutes(30).Year
                });
        }


        // ---------------------------------------------------------
        // PAYROLL PROCESSING PAGE
        // ---------------------------------------------------------

        [HttpGet]
        public async Task<IActionResult> Process(
    int employeeId,
    int? month,
    int? year)
        {
            var today = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30);

            var selectedMonth = month ?? today.Month;
            var selectedYear = year ?? today.Year;

            if (selectedMonth < 1 || selectedMonth > 12)
                return BadRequest();

            if (selectedYear < 2000 || selectedYear > 2100)
                return BadRequest();

            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            var monthStart =
     new DateTime(selectedYear, selectedMonth, 1);

            var nextMonthStart =
                monthStart.AddMonths(1);

            var salary = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s =>
                    s.EmployeeId == employee.Id &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (salary == null)
            {
                TempData["Error"] =
                    "Salary structure has not been configured for this employee.";

                return RedirectToAction(
                    nameof(Index),
                    new
                    {
                        month = selectedMonth,
                        year = selectedYear
                    });
            }

            var deductionSettings =
            await GetActivePayrollDeductionSettingsAsync();

            var payroll = await _db.Payrolls
                .FirstOrDefaultAsync(p =>
                    p.EmployeeId == employee.Id &&
                    p.Month == selectedMonth &&
                    p.Year == selectedYear);

            // ---------------------------------------------------------
            // MONTH DATES
            // ---------------------------------------------------------

            var todayIndia = today.Date;

            DateTime lastDateToEvaluate;

            if (selectedYear < todayIndia.Year ||
                (selectedYear == todayIndia.Year &&
                 selectedMonth < todayIndia.Month))
            {
                lastDateToEvaluate =
                    nextMonthStart.AddDays(-1);
            }
            else if (selectedYear == todayIndia.Year &&
                     selectedMonth == todayIndia.Month)
            {
                lastDateToEvaluate = todayIndia;
            }
            else
            {
                // Future payroll month.
                lastDateToEvaluate = monthStart.AddDays(-1);
            }

            // ---------------------------------------------------------
            // ATTENDANCE
            // ---------------------------------------------------------

            var attendance = await _db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.EmployeeId == employee.Id &&
                    a.AttendanceDate >= GetIndiaAttendanceDate(monthStart) &&
                    a.AttendanceDate < GetIndiaAttendanceDate(nextMonthStart))
                .ToListAsync();

            // ---------------------------------------------------------
            // APPROVED LEAVE
            // ---------------------------------------------------------
            var approvedLeaves = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.EmployeeId == employee.Id &&
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate.Date < nextMonthStart &&
                    l.ToDate.Date >= monthStart)
                .ToListAsync();

            // ---------------------------------------------------------
            // PAID LEAVE ENTITLEMENT
            // ---------------------------------------------------------

            var leaveYearStart = new DateTime(selectedYear, 1, 1);
            var leaveYearEnd = leaveYearStart.AddYears(1);

            var approvedLeavesForYear = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.EmployeeId == employee.Id &&
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate.Date >= leaveYearStart &&
                    l.FromDate.Date < leaveYearEnd)
                .OrderBy(l => l.FromDate)
                .ThenBy(l => l.Id)
                .ToListAsync();

            decimal paidLeaveUsedBeforeMonth = 0m;

            foreach (var leave in approvedLeavesForYear)
            {
                // Ignore leaves that start in the current/future month.
                if (leave.FromDate.Date >= monthStart)
                    continue;

                // Calculate only the portion of this leave
                // that occurred before the current payroll month.
                var leaveStart = leave.FromDate.Date;
                var leaveEnd = leave.ToDate.Date;

                var effectiveEnd = leaveEnd < monthStart
                    ? leaveEnd
                    : monthStart.AddDays(-1);

                if (effectiveEnd < leaveStart)
                    continue;

                var leaveDaysBeforeMonth = 0m;

                for (
                    var leaveDay = leaveStart;
                    leaveDay <= effectiveEnd;
                    leaveDay = leaveDay.AddDays(1))
                {
                    // Weekly offs are not counted as leave days.
                    var isSunday =
                        leaveDay.DayOfWeek == DayOfWeek.Sunday;

                    var isSaturday =
                        leaveDay.DayOfWeek == DayOfWeek.Saturday;

                    var weekOfMonth =
                        ((leaveDay.Day - 1) / 7) + 1;

                    var isSecondSaturday =
                        isSaturday && weekOfMonth == 2;

                    var isFourthSaturday =
                        isSaturday && weekOfMonth == 4;

                    if (isSunday ||
                        isSecondSaturday ||
                        isFourthSaturday)
                    {
                        continue;
                    }

                    leaveDaysBeforeMonth += 1m;
                }

                // Preserve half-day leave.
                if (leave.TotalDays == 0.5m &&
                    leaveStart < monthStart)
                {
                    leaveDaysBeforeMonth = 0.5m;
                }

                paidLeaveUsedBeforeMonth += leaveDaysBeforeMonth;
            }

            decimal paidLeaveUsed = paidLeaveUsedBeforeMonth;

            const decimal annualPaidLeaveEntitlement = 10m;

            decimal workingDays = 0;
            decimal paidDays = 0;
            decimal lop = 0;

            if (lastDateToEvaluate >= monthStart)
            {
                for (
                    var day = monthStart;
                    day <= lastDateToEvaluate;
                    day = day.AddDays(1))
                {
                    // Employee had not joined yet.
                    if (employee.JoiningDate.HasValue &&
                        day.Date < employee.JoiningDate.Value.Date)
                    {
                        continue;
                    }

                    var isSunday =
                        day.DayOfWeek == DayOfWeek.Sunday;

                    var isSaturday =
                        day.DayOfWeek == DayOfWeek.Saturday;

                    var weekOfMonth =
                        ((day.Day - 1) / 7) + 1;

                    var isSecondSaturday =
                        isSaturday && weekOfMonth == 2;

                    var isFourthSaturday =
                        isSaturday && weekOfMonth == 4;

                    var isWeeklyOff =
                        isSunday ||
                        isSecondSaturday ||
                        isFourthSaturday;

                    if (isWeeklyOff)
                        continue;

                    workingDays++;

                    var attendanceDate =
                        GetIndiaAttendanceDate(day);

                    var dayAttendance =
                        attendance.FirstOrDefault(a =>
                            a.AttendanceDate == attendanceDate);

                    // -------------------------------------------------
                    // ATTENDANCE EXISTS
                    // -------------------------------------------------

                    if (dayAttendance != null)
                    {
                        if (dayAttendance.Status == "Present")
                        {
                            paidDays += 1;
                            continue;
                        }

                        if (dayAttendance.Status == "Half Day")
                        {
                            paidDays += 0.5m;
                            continue;
                        }

                        if (dayAttendance.Status == "No Punch-In" ||
                            dayAttendance.Status == "No Out-Punch")
                        {
                            // Existing rule:
                            // one missing punch still counts as present.
                            paidDays += 1;
                            continue;
                        }

                        if (dayAttendance.Status == "On Leave")
                        {
                            var approvedLeaveForAttendance = approvedLeaves
                                .FirstOrDefault(l =>
                                    l.FromDate.Date <= day.Date &&
                                    l.ToDate.Date >= day.Date);

                            if (approvedLeaveForAttendance != null)
                            {
                                var leaveDayValue =
    approvedLeaveForAttendance.TotalDays == 0.5m
        ? 0.5m
        : 1m;

                                var paidLeaveRemaining =
                                    Math.Max(
                                        0m,
                                        annualPaidLeaveEntitlement - paidLeaveUsed);

                                var paidLeaveForDay =
                                    Math.Min(
                                        leaveDayValue,
                                        paidLeaveRemaining);

                                paidDays += paidLeaveForDay;

                                var unpaidLeaveForDay =
                                    leaveDayValue - paidLeaveForDay;

                                lop += unpaidLeaveForDay;

                                paidLeaveUsed += paidLeaveForDay;
                            }
                            else
                            {
                                // On Leave without an approved leave record
                                // is not automatically treated as paid leave.
                                lop += 1;
                            }

                            continue;
                        }
                    }

                    // ---------------------------------------------------------
                    // APPROVED LEAVE WITHOUT ATTENDANCE
                    // ---------------------------------------------------------

                    var approvedLeaveWithoutAttendance =
                        approvedLeaves.FirstOrDefault(l =>
                            l.FromDate.Date <= day.Date &&
                            l.ToDate.Date >= day.Date);

                    if (approvedLeaveWithoutAttendance != null)
                    {
                        var leaveDayValue =
                            approvedLeaveWithoutAttendance.TotalDays == 0.5m
                                ? 0.5m
                                : 1m;

                        var paidLeaveRemaining =
                            Math.Max(
                                0m,
                                annualPaidLeaveEntitlement - paidLeaveUsed);

                        var paidLeaveForDay =
                            Math.Min(
                                leaveDayValue,
                                paidLeaveRemaining);

                        paidDays += paidLeaveForDay;

                        var unpaidLeaveForDay =
                            leaveDayValue - paidLeaveForDay;

                        lop += unpaidLeaveForDay;

                        paidLeaveUsed += paidLeaveForDay;
                    }
                    else
                    {
                        // No attendance and no approved leave.
                        lop += 1;
                    }
                }
            }

            // ---------------------------------------------------------
            // FUTURE MONTH
            // ---------------------------------------------------------

            if (lastDateToEvaluate < monthStart)
            {
                workingDays = 0;
                paidDays = 0;
                lop = 0;
            }

            // ---------------------------------------------------------
            // GROSS MONTHLY SALARY
            // ---------------------------------------------------------

            var monthlyGross =
                salary.BasicSalary +
                salary.HRA +
                salary.ConveyanceAllowance +
                salary.WashingAllowance +
                salary.OtherAllowances;

            decimal earnedGross = 0;

            if (workingDays > 0)
            {
                var dailySalary =
                    monthlyGross / workingDays;

                earnedGross =
                    dailySalary * paidDays;
            }


            var isCurrentMonth =
                selectedYear == todayIndia.Year &&
                selectedMonth == todayIndia.Month;

            var isPastMonth =
                selectedYear < todayIndia.Year ||
                (selectedYear == todayIndia.Year &&
                 selectedMonth < todayIndia.Month);

            var isFutureMonth =
                selectedYear > todayIndia.Year ||
                (selectedYear == todayIndia.Year &&
                 selectedMonth > todayIndia.Month);

            ViewBag.Month = selectedMonth;
            ViewBag.Year = selectedYear;

            ViewBag.Employee = employee;
            ViewBag.Salary = salary;
            ViewBag.Payroll = payroll;

            ViewBag.WorkingDays = workingDays;
            ViewBag.PayDays = paidDays;
            ViewBag.PaidDays = paidDays;
            ViewBag.LOP = lop;

            ViewBag.MonthlyGross = monthlyGross;
            ViewBag.EarnedGross = Math.Round(
                earnedGross,
                2,
                MidpointRounding.AwayFromZero);

            ViewBag.PF =
    salary.PFApplicable
        ? deductionSettings?.PF ?? 0m
        : 0m;

            ViewBag.ESI =
                salary.ESICApplicable
                    ? deductionSettings?.ESIC ?? 0m
                    : 0m;

            ViewBag.ProfessionalTax =
                salary.ProfessionalTaxApplicable
                    ? deductionSettings?.ProfessionalTax ?? 0m
                    : 0m;

            ViewBag.LWF =
                salary.LWFApplicable
                    ? deductionSettings?.LWF ?? 0m
                    : 0m;

            ViewBag.IsCurrentMonth = isCurrentMonth;
            ViewBag.IsPastMonth = isPastMonth;
            ViewBag.IsFutureMonth = isFutureMonth;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Process(
    int employeeId,
    int month,
    int year,
    decimal pf,
    decimal esi,
    decimal professionalTax,
    decimal lwf,
    decimal otherDeductions,
    string? remarks,
    string? action)
        {
            if (!User.IsInRole("HR"))
                return Forbid();

            var todayIndia = DateTime.UtcNow
    .AddHours(5)
    .AddMinutes(30)
    .Date;

            var isCurrentMonth =
                year == todayIndia.Year &&
                month == todayIndia.Month;

            var isPastMonth =
                year < todayIndia.Year ||
                (year == todayIndia.Year &&
                 month < todayIndia.Month);

            var isFutureMonth =
                year > todayIndia.Year ||
                (year == todayIndia.Year &&
                 month > todayIndia.Month);

            if (isFutureMonth)
            {
                TempData["Error"] =
                    "Payroll preview is not available for a future month.";

                return RedirectToAction(
                    nameof(Index),
                    new { month, year });
            }

            var lastDayOfMonth = new DateTime(
    year,
    month,
    DateTime.DaysInMonth(year, month));

            var isLastThreeDays =
                year == todayIndia.Year &&
                month == todayIndia.Month &&
                todayIndia.Date >= lastDayOfMonth.AddDays(-2);

            var canProcessPayroll =
                isPastMonth || isLastThreeDays;

            

            if (action == "Process" && !canProcessPayroll)
            {
                TempData["Error"] =
                    "Payroll can only be processed during the last 3 days of the month or after the month is complete.";

                return RedirectToAction(
                    nameof(Process),
                    new
                    {
                        employeeId,
                        month,
                        year
                    });
            }

            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            var monthStart =
    new DateTime(year, month, 1);

            var nextMonthStart =
                monthStart.AddMonths(1);

            var salary = await _db.SalaryMasters
                .Where(s =>
                    s.EmployeeId == employee.Id &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .FirstOrDefaultAsync();

            if (salary == null)
            {
                TempData["Error"] =
                    "Salary structure has not been configured for this employee.";

                return RedirectToAction(
                    nameof(Index),
                    new { month, year });
            }

            var deductionSettings =
    await GetActivePayrollDeductionSettingsAsync();

            if (deductionSettings == null)
            {
                TempData["Error"] =
                    "Payroll deduction settings have not been configured correctly. Please configure PF, ESIC, Professional Tax and LWF before processing payroll.";

                return RedirectToAction(
                    nameof(DeductionSettings));
            }

            var existingPayroll = await _db.Payrolls
                .FirstOrDefaultAsync(p =>
                    p.EmployeeId == employee.Id &&
                    p.Month == month &&
                    p.Year == year);

            if (existingPayroll != null &&
                existingPayroll.Status == "Finalized")
            {
                TempData["Error"] =
                    "This payroll has already been finalized and cannot be changed.";

                return RedirectToAction(
                    nameof(Index),
                    new { month, year });
            }

            // ---------------------------------------------------------
            // RE-CALCULATE PAYROLL VALUES
            // ---------------------------------------------------------

            DateTime lastDateToEvaluate;

            if (year < todayIndia.Year ||
                (year == todayIndia.Year &&
                 month < todayIndia.Month))
            {
                lastDateToEvaluate =
                    nextMonthStart.AddDays(-1);
            }
            else if (year == todayIndia.Year &&
                     month == todayIndia.Month)
            {
                lastDateToEvaluate = todayIndia;
            }
            else
            {
                lastDateToEvaluate = monthStart.AddDays(-1);
            }

            var attendance = await _db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.EmployeeId == employee.Id &&
                    a.AttendanceDate >= GetIndiaAttendanceDate(monthStart) &&
                    a.AttendanceDate < GetIndiaAttendanceDate(nextMonthStart))
                .ToListAsync();

            var approvedLeaves = await _db.Leaves
    .AsNoTracking()
    .Where(l =>
        l.EmployeeId == employee.Id &&
        l.Status == "Approved" &&
        l.CancelledOn == null &&
        l.FromDate.Date < nextMonthStart &&
        l.ToDate.Date >= monthStart)
    .ToListAsync();

            // ---------------------------------------------------------
            // PAID LEAVE ENTITLEMENT
            // ---------------------------------------------------------

            const decimal annualPaidLeaveEntitlement = 10m;

            var leaveYearStart =
                new DateTime(year, 1, 1);

            var leaveYearEnd =
                leaveYearStart.AddYears(1);

            var approvedLeavesForYear = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.EmployeeId == employee.Id &&
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate.Date >= leaveYearStart &&
                    l.FromDate.Date < leaveYearEnd)
                .OrderBy(l => l.FromDate)
                .ThenBy(l => l.Id)
                .ToListAsync();

            decimal paidLeaveUsedBeforeMonth = 0m;

            foreach (var leave in approvedLeavesForYear)
            {
                // Ignore leaves that start in the current/future month.
                if (leave.FromDate.Date >= monthStart)
                    continue;

                // Calculate only the portion of this leave
                // that occurred before the current payroll month.
                var leaveStart = leave.FromDate.Date;
                var leaveEnd = leave.ToDate.Date;

                var effectiveEnd = leaveEnd < monthStart
                    ? leaveEnd
                    : monthStart.AddDays(-1);

                if (effectiveEnd < leaveStart)
                    continue;

                var leaveDaysBeforeMonth = 0m;

                for (
                    var leaveDay = leaveStart;
                    leaveDay <= effectiveEnd;
                    leaveDay = leaveDay.AddDays(1))
                {
                    // Weekly offs are not counted as leave days.
                    var isSunday =
                        leaveDay.DayOfWeek == DayOfWeek.Sunday;

                    var isSaturday =
                        leaveDay.DayOfWeek == DayOfWeek.Saturday;

                    var weekOfMonth =
                        ((leaveDay.Day - 1) / 7) + 1;

                    var isSecondSaturday =
                        isSaturday && weekOfMonth == 2;

                    var isFourthSaturday =
                        isSaturday && weekOfMonth == 4;

                    if (isSunday ||
                        isSecondSaturday ||
                        isFourthSaturday)
                    {
                        continue;
                    }

                    leaveDaysBeforeMonth += 1m;
                }

                // Preserve half-day leave.
                if (leave.TotalDays == 0.5m &&
                    leaveStart < monthStart)
                {
                    leaveDaysBeforeMonth = 0.5m;
                }

                paidLeaveUsedBeforeMonth += leaveDaysBeforeMonth;
            }

            decimal paidLeaveUsed = paidLeaveUsedBeforeMonth;

            decimal workingDays = 0;
            decimal paidDays = 0;
            decimal lop = 0;

            if (lastDateToEvaluate >= monthStart)
            {
                for (
                    var day = monthStart;
                    day <= lastDateToEvaluate;
                    day = day.AddDays(1))
                {
                    if (employee.JoiningDate.HasValue &&
                        day.Date < employee.JoiningDate.Value.Date)
                    {
                        continue;
                    }

                    var isSunday =
                        day.DayOfWeek == DayOfWeek.Sunday;

                    var isSaturday =
                        day.DayOfWeek == DayOfWeek.Saturday;

                    var weekOfMonth =
                        ((day.Day - 1) / 7) + 1;

                    var isSecondSaturday =
                        isSaturday && weekOfMonth == 2;

                    var isFourthSaturday =
                        isSaturday && weekOfMonth == 4;

                    if (isSunday ||
                        isSecondSaturday ||
                        isFourthSaturday)
                    {
                        continue;
                    }

                    workingDays++;

                    var attendanceDate =
                        GetIndiaAttendanceDate(day);

                    var dayAttendance =
                        attendance.FirstOrDefault(a =>
                            a.AttendanceDate == attendanceDate);

                    // ---------------------------------------------------------
                    // ATTENDANCE EXISTS
                    // ---------------------------------------------------------

                    if (dayAttendance != null)
                    {
                        if (dayAttendance.Status == "Present")
                        {
                            paidDays += 1m;
                            continue;
                        }

                        if (dayAttendance.Status == "Half Day")
                        {
                            paidDays += 0.5m;
                            continue;
                        }

                        if (dayAttendance.Status == "No Punch-In" ||
                            dayAttendance.Status == "No Out-Punch")
                        {
                            // Existing attendance rule:
                            // one missing punch still counts as present.
                            paidDays += 1m;
                            continue;
                        }

                        if (dayAttendance.Status == "On Leave")
                        {
                            var approvedLeaveForAttendance = approvedLeaves
                                .FirstOrDefault(l =>
                                    l.FromDate.Date <= day.Date &&
                                    l.ToDate.Date >= day.Date);

                            if (approvedLeaveForAttendance != null)
                            {
                                // A normal leave day = 1 day.
                                // A half-day leave = 0.5 day.
                                var leaveDayValue =
                                    approvedLeaveForAttendance.TotalDays == 0.5m
                                        ? 0.5m
                                        : 1m;

                                var paidLeaveRemaining =
                                    Math.Max(
                                        0m,
                                        annualPaidLeaveEntitlement - paidLeaveUsed);

                                var paidLeaveForDay =
                                    Math.Min(
                                        leaveDayValue,
                                        paidLeaveRemaining);

                                var unpaidLeaveForDay =
                                    leaveDayValue - paidLeaveForDay;

                                paidDays += paidLeaveForDay;
                                lop += unpaidLeaveForDay;

                                paidLeaveUsed += paidLeaveForDay;
                            }
                            else
                            {
                                // "On Leave" attendance without an approved
                                // leave application is unpaid.
                                lop += 1m;
                            }

                            continue;
                        }
                    }

                    // ---------------------------------------------------------
                    // APPROVED LEAVE WITHOUT ATTENDANCE
                    // ---------------------------------------------------------

                    var approvedLeave =
                        approvedLeaves.FirstOrDefault(l =>
                            l.FromDate.Date <= day.Date &&
                            l.ToDate.Date >= day.Date);

                    if (approvedLeave != null)
                    {
                        // A normal leave day = 1 day.
                        // A half-day leave = 0.5 day.
                        var leaveDayValue =
                            approvedLeave.TotalDays == 0.5m
                                ? 0.5m
                                : 1m;

                        var paidLeaveRemaining =
                            Math.Max(
                                0m,
                                annualPaidLeaveEntitlement - paidLeaveUsed);

                        var paidLeaveForDay =
                            Math.Min(
                                leaveDayValue,
                                paidLeaveRemaining);

                        var unpaidLeaveForDay =
                            leaveDayValue - paidLeaveForDay;

                        paidDays += paidLeaveForDay;
                        lop += unpaidLeaveForDay;

                        paidLeaveUsed += paidLeaveForDay;
                    }
                    else
                    {
                        // No attendance and no approved leave.
                        lop += 1m;
                    }
                }
            }

            // ---------------------------------------------------------
            // SALARY
            // ---------------------------------------------------------

            var monthlyGross =
                salary.BasicSalary +
                salary.HRA +
                salary.ConveyanceAllowance +
                salary.WashingAllowance +
                salary.OtherAllowances;

            decimal earnedGross = 0;

            if (workingDays > 0)
            {
                earnedGross =
                    (monthlyGross / workingDays) * paidDays;
            }

            earnedGross =
                Math.Round(
                    earnedGross,
                    2,
                    MidpointRounding.AwayFromZero);

            // ---------------------------------------------------------
            // DEDUCTIONS
            // ---------------------------------------------------------
            //
            // PF / ESIC / Professional Tax / LWF are controlled by
            // company-level PayrollDeductionSettings.
            //
            // Employee-level SalaryMaster applicability flags decide
            // whether each configured deduction actually applies.
            //
            // The posted PF / ESI / PT / LWF values are intentionally
            // ignored so they cannot override company configuration.
            //

            pf = salary.PFApplicable
                ? Math.Max(0m, deductionSettings.PF!.Value)
                : 0m;

            esi = salary.ESICApplicable
                ? Math.Max(0m, deductionSettings.ESIC!.Value)
                : 0m;

            professionalTax = salary.ProfessionalTaxApplicable
                ? Math.Max(0m, deductionSettings.ProfessionalTax!.Value)
                : 0m;

            lwf = salary.LWFApplicable
                ? Math.Max(0m, deductionSettings.LWF!.Value)
                : 0m;

            otherDeductions = Math.Max(0m, otherDeductions);

            var totalDeductions =
                pf +
                esi +
                professionalTax +
                lwf +
                otherDeductions;

            totalDeductions =
                Math.Round(
                    totalDeductions,
                    2,
                    MidpointRounding.AwayFromZero);

            var netSalary =
                earnedGross - totalDeductions;

            netSalary =
                Math.Round(
                    netSalary,
                    2,
                    MidpointRounding.AwayFromZero);

            // ---------------------------------------------------------
            // SAVE / UPDATE PAYROLL
            // ---------------------------------------------------------

            if (existingPayroll == null)
            {
                existingPayroll = new Payroll
                {
                    EmployeeId = employee.Id,
                    Month = month,
                    Year = year,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Payrolls.Add(existingPayroll);
            }

            existingPayroll.BasicSalary =
                salary.BasicSalary;

            existingPayroll.HRA =
                salary.HRA;

            existingPayroll.ConveyanceAllowance =
                salary.ConveyanceAllowance;

            existingPayroll.WashingAllowance =
                salary.WashingAllowance;

            existingPayroll.OtherAllowances =
                salary.OtherAllowances;

            existingPayroll.GrossSalary =
                earnedGross;

            existingPayroll.PF =
                pf;

            existingPayroll.ESI =
                esi;

            existingPayroll.ProfessionalTax =
                professionalTax;

            existingPayroll.LWF =
                lwf;

            existingPayroll.OtherDeductions =
                otherDeductions;

            existingPayroll.TotalDeductions =
                totalDeductions;

            existingPayroll.NetSalary =
                netSalary;

            existingPayroll.WorkingDays =
                workingDays;

            existingPayroll.PayDays =
                paidDays;

            existingPayroll.PaidDays =
                paidDays;

            existingPayroll.LOP =
                lop;

            existingPayroll.Status =
     action == "Preview"
         ? "Draft"
         : "Processed";

            existingPayroll.Remarks =
                string.IsNullOrWhiteSpace(remarks)
                    ? null
                    : remarks.Trim();

            existingPayroll.UpdatedAt =
                DateTime.UtcNow;

            // ---------------------------------------------------------
            // PREVIEW ONLY
            // ---------------------------------------------------------

            if (action == "Preview")
            {
                ViewBag.Month = month;
                ViewBag.Year = year;

                ViewBag.Employee = employee;
                ViewBag.Salary = salary;
                ViewBag.Payroll = existingPayroll;

                ViewBag.WorkingDays = workingDays;
                ViewBag.PayDays = paidDays;
                ViewBag.PaidDays = paidDays;
                ViewBag.LOP = lop;

                ViewBag.MonthlyGross = monthlyGross;
                ViewBag.EarnedGross = earnedGross;

                ViewBag.PF = pf;
                ViewBag.ESI = esi;
                ViewBag.ProfessionalTax = professionalTax;
                ViewBag.LWF = lwf;
                ViewBag.OtherDeductions = otherDeductions;

                ViewBag.TotalDeductions = totalDeductions;
                ViewBag.NetSalary = netSalary;

                ViewBag.IsCurrentMonth =
                    year == todayIndia.Year &&
                    month == todayIndia.Month;

                ViewBag.IsPastMonth =
                    year < todayIndia.Year ||
                    (year == todayIndia.Year &&
                     month < todayIndia.Month);

                ViewBag.IsFutureMonth = false;

                ViewBag.IsPreview = true;

                await _db.SaveChangesAsync();

                return View();
            }


            // ---------------------------------------------------------
            // SAVE / PROCESS PAYROLL
            // ---------------------------------------------------------

            if (existingPayroll == null)
            {
                existingPayroll = new Payroll
                {
                    EmployeeId = employee.Id,
                    Month = month,
                    Year = year,
                    CreatedAt = DateTime.UtcNow
                };

                _db.Payrolls.Add(existingPayroll);
            }

            existingPayroll.BasicSalary =
                salary.BasicSalary;

            existingPayroll.HRA =
                salary.HRA;

            existingPayroll.ConveyanceAllowance =
                salary.ConveyanceAllowance;

            existingPayroll.WashingAllowance =
                salary.WashingAllowance;

            existingPayroll.OtherAllowances =
                salary.OtherAllowances;

            existingPayroll.GrossSalary =
                earnedGross;

            existingPayroll.PF =
                pf;

            existingPayroll.ESI =
                esi;

            existingPayroll.ProfessionalTax =
                professionalTax;

            existingPayroll.LWF =
                lwf;

            existingPayroll.OtherDeductions =
                otherDeductions;

            existingPayroll.TotalDeductions =
                totalDeductions;

            existingPayroll.NetSalary =
                netSalary;

            existingPayroll.WorkingDays =
                workingDays;

            existingPayroll.PayDays =
                paidDays;

            existingPayroll.PaidDays =
                paidDays;

            existingPayroll.LOP =
                lop;

            existingPayroll.Status =
                "Processed";

            existingPayroll.Remarks =
                string.IsNullOrWhiteSpace(remarks)
                    ? null
                    : remarks.Trim();

            existingPayroll.UpdatedAt =
                DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Payroll processed successfully.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    month,
                    year
                });
        }
        // ---------------------------------------------------------
        // FINALIZE PAYROLL
        // ---------------------------------------------------------

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Finalize(
            int employeeId,
            int month,
            int year)
        {
            // ---------------------------------------------------------
            // VALIDATE MONTH
            // ---------------------------------------------------------

            var todayIndia = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30)
                .Date;

            var isPastMonth =
                year < todayIndia.Year ||
                (year == todayIndia.Year &&
                 month < todayIndia.Month);
            var lastDayOfMonth = new DateTime(
                year,
                month,
                DateTime.DaysInMonth(year, month));

            var isLastThreeDays =
                year == todayIndia.Year &&
                month == todayIndia.Month &&
                todayIndia.Date >= lastDayOfMonth.AddDays(-2);

            var canFinalizePayroll =
                isPastMonth || isLastThreeDays;

            if (!canFinalizePayroll)
            {
                TempData["Error"] =
                    "Payroll finalization is available during the last 3 days of the month or after the month has ended.";

                return RedirectToAction(
                    nameof(Process),
                    new
                    {
                        employeeId,
                        month,
                        year
                    });
            }

            // ---------------------------------------------------------
            // FIND EMPLOYEE
            // ---------------------------------------------------------

            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            // ---------------------------------------------------------
            // FIND PAYROLL
            // ---------------------------------------------------------

            var payroll = await _db.Payrolls
                .FirstOrDefaultAsync(p =>
                    p.EmployeeId == employee.Id &&
                    p.Month == month &&
                    p.Year == year);

            if (payroll == null)
            {
                TempData["Error"] =
                    "Payroll record was not found.";

                return RedirectToAction(
                    nameof(Index),
                    new
                    {
                        month,
                        year
                    });
            }

            // ---------------------------------------------------------
            // PREVENT RE-FINALIZATION
            // ---------------------------------------------------------

            if (payroll.Status == "Finalized")
            {
                TempData["Error"] =
                    "This payroll has already been finalized.";

                return RedirectToAction(
                    nameof(Index),
                    new
                    {
                        month,
                        year
                    });
            }

            // ---------------------------------------------------------
            // FINALIZE ONLY PROCESSED PAYROLL
            // ---------------------------------------------------------

            if (payroll.Status != "Processed")
            {
                TempData["Error"] =
                    "Only processed payroll can be finalized.";

                return RedirectToAction(
                    nameof(Process),
                    new
                    {
                        employeeId,
                        month,
                        year
                    });
            }

            // ---------------------------------------------------------
            // FINALIZE
            // ---------------------------------------------------------

            payroll.Status = "Finalized";
            payroll.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Payroll finalized successfully.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    month,
                    year
                });
        }

        // ---------------------------------------------------------
        // PAYSLIP
        // Official payslip is available only for finalized payroll.
        // ---------------------------------------------------------

        [Authorize(Roles = "HR,Management")]
        [HttpGet]
        public async Task<IActionResult> Payslip(
            int employeeId,
            int month,
            int year)
        {
            // ---------------------------------------------------------
            // FIND EMPLOYEE
            // ---------------------------------------------------------

            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                    .ThenInclude(u => u.Company)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            // ---------------------------------------------------------
            // FIND FINALIZED PAYROLL
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
                    nameof(Index),
                    new
                    {
                        month,
                        year
                    });
            }

            // ---------------------------------------------------------
            // ONLY FINALIZED PAYROLL CAN GENERATE OFFICIAL PAYSLIP
            // ---------------------------------------------------------

            if (payroll.Status != "Finalized")
            {
                TempData["Error"] =
                    "Payslip can only be generated after payroll has been finalized.";

                return RedirectToAction(
                    nameof(Index),
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
            // PASS DATA TO PAYSLIP VIEW
            // ---------------------------------------------------------

            ViewBag.Employee = employee;
            ViewBag.Payroll = payroll;
            ViewBag.Salary = salary;

            ViewBag.Month = month;
            ViewBag.Year = year;

            return View();
        }

        // ---------------------------------------------------------
        // PAYROLL DEDUCTION SETTINGS
        // ---------------------------------------------------------

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> DeductionSettings()
        {
            var now = DateTime.UtcNow;

            var configuration = await _db.HRMConfigurations
                .AsNoTracking()
                .Where(c =>
                    c.CompanyId == CurrentCompanyId &&
                    c.ConfigurationType == "PayrollDeduction" &&
                    c.ConfigurationCode == "DEFAULT" &&
                    c.IsActive &&
                    c.EffectiveFrom <= now &&
                    (
                        c.EffectiveTo == null ||
                        c.EffectiveTo >= now
                    ))
                .OrderByDescending(c => c.Version)
                .FirstOrDefaultAsync();

            PayrollDeductionSettings settings;

            if (configuration == null ||
                string.IsNullOrWhiteSpace(configuration.ConfigurationJson))
            {
                settings = new PayrollDeductionSettings();
            }
            else
            {
                try
                {
                    settings =
                        JsonSerializer.Deserialize<PayrollDeductionSettings>(
                            configuration.ConfigurationJson,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            })
                        ?? new PayrollDeductionSettings();
                }
                catch
                {
                    settings = new PayrollDeductionSettings();
                }
            }

            ViewBag.Configuration = configuration;

            return View(settings);
        }

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeductionSettings(
    PayrollDeductionSettings settings)
        {
            if (!ModelState.IsValid)
            {
                return View(settings);
            }

            if (!settings.PF.HasValue ||
                !settings.ESIC.HasValue ||
                !settings.ProfessionalTax.HasValue ||
                !settings.LWF.HasValue)
            {
                ModelState.AddModelError(
                    "",
                    "All four deduction amounts are required.");

                return View(settings);
            }

            if (settings.PF.Value < 0 ||
                settings.ESIC.Value < 0 ||
                settings.ProfessionalTax.Value < 0 ||
                settings.LWF.Value < 0)
            {
                ModelState.AddModelError(
                    "",
                    "Deduction amounts cannot be negative.");

                return View(settings);
            }

            var now = DateTime.UtcNow;

            var currentConfiguration =
                await _db.HRMConfigurations
                    .Where(c =>
                        c.CompanyId == CurrentCompanyId &&
                        c.ConfigurationType == "PayrollDeduction" &&
                        c.ConfigurationCode == "DEFAULT" &&
                        c.IsActive)
                    .OrderByDescending(c => c.Version)
                    .FirstOrDefaultAsync();

            var newSettings = new PayrollDeductionSettings
            {
                PF = settings.PF.Value,
                ESIC = settings.ESIC.Value,
                ProfessionalTax = settings.ProfessionalTax.Value,
                LWF = settings.LWF.Value
            };

            var configurationJson =
                JsonSerializer.Serialize(newSettings);

            // ---------------------------------------------------------
            // If the values have not changed, do not create a new version.
            // ---------------------------------------------------------

            if (currentConfiguration != null &&
                string.Equals(
                    currentConfiguration.ConfigurationJson,
                    configurationJson,
                    StringComparison.Ordinal))
            {
                TempData["Success"] =
                    "Payroll deduction settings are already up to date.";

                return RedirectToAction(
                    nameof(DeductionSettings));
            }

            // ---------------------------------------------------------
            // Archive previous active configuration.
            // ---------------------------------------------------------

            if (currentConfiguration != null)
            {
                currentConfiguration.IsActive = false;
                currentConfiguration.EffectiveTo = now;
                currentConfiguration.UpdatedAt = now;
            }

            var nextVersion =
                currentConfiguration == null
                    ? 1
                    : currentConfiguration.Version + 1;

            var newConfiguration = new HRMConfiguration
            {
                CompanyId = CurrentCompanyId,
                ConfigurationType = "PayrollDeduction",
                ConfigurationCode = "DEFAULT",
                ConfigurationJson = configurationJson,
                EffectiveFrom = now,
                EffectiveTo = null,
                Version = nextVersion,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.HRMConfigurations.Add(newConfiguration);

            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Payroll deduction settings updated successfully.";

            return RedirectToAction(
                nameof(DeductionSettings));
        }

    }




}