using ExpenseManagement.Data;
using ExpenseManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Services;
using System.Text.Json;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "HR,Management")]
    public class PayrollController : Controller
    {
        private readonly AppDbContext _db;
        private readonly LeavePolicyService _leavePolicy;

        private void WriteAudit(string action, int? entityId, string description, string entityName = "Payroll")
        {
            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = CurrentCompanyId,
                UserId = CurrentUserId,
                Action = action,
                EntityName = entityName,
                EntityId = entityId,
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }

        public PayrollController(AppDbContext db, LeavePolicyService leavePolicy)
        {
            _db = db;
            _leavePolicy = leavePolicy;
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


            var salaryHistory = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s => s.EmployeeId == employee.Id)
                .OrderByDescending(s => s.EffectiveFrom)
                .ToListAsync();

            var todayIst = DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            var effectiveSalary =
                salaryHistory.FirstOrDefault(s => s.EffectiveFrom.Date <= todayIst)
                ?? salaryHistory.FirstOrDefault();

            ViewBag.Employee = employee;
            ViewBag.Salary = effectiveSalary;
            ViewBag.SalaryHistory = salaryHistory;
            ViewBag.EffectiveSalaryId = effectiveSalary?.Id;

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
            var todayIst = DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;
            var now = DateTime.UtcNow;
            var revisionDate = DateTime.SpecifyKind(effectiveFrom.Date, DateTimeKind.Utc);

            var revisions = await _db.SalaryMasters
                .Where(s => s.EmployeeId == employee.Id)
                .ToListAsync();

            string? revisionError = null;

            if (revisions.Any(s => s.EffectiveFrom.Date == revisionDate.Date))
            {
                revisionError =
                    $"A salary revision effective {revisionDate:dd-MMM-yyyy} already exists for this employee.";
            }
            else if (employee.JoiningDate.HasValue &&
                     revisionDate.Date < employee.JoiningDate.Value.Date)
            {
                revisionError = "The Effective From date cannot be before the joining date.";
            }

            if (revisionError != null)
            {
                var history = revisions.OrderByDescending(s => s.EffectiveFrom).ToList();
                var inForce = history.FirstOrDefault(s => s.EffectiveFrom.Date <= todayIst)
                              ?? history.FirstOrDefault();

                ModelState.AddModelError("", revisionError);

                ViewBag.Employee = employee;
                ViewBag.Salary = inForce;
                ViewBag.SalaryHistory = history;
                ViewBag.EffectiveSalaryId = inForce?.Id;

                return View();
            }

            // The revision with the latest effective date is the only one
            // flagged IsActive. A backdated revision does not take that flag.
            var head = revisions.OrderByDescending(s => s.EffectiveFrom).FirstOrDefault();
            var becomesHead = head == null || revisionDate.Date > head.EffectiveFrom.Date;

            await using var tx = await _db.Database.BeginTransactionAsync();

            // PAN, UAN and ESIC belong to the employee and are stored once,
            // on the employee profile.
            static string? Clean(string? value) =>
                string.IsNullOrWhiteSpace(value) ? null : value.Trim();

            var newPan = Clean(panNumber);
            var newUan = Clean(uanNumber);
            var newEsic = Clean(esicNumber);

            var statutoryChanged =
                employee.PANNumber != newPan ||
                employee.UANNumber != newUan ||
                employee.ESICNumber != newEsic;

            if (statutoryChanged)
            {
                employee.PANNumber = newPan;
                employee.UANNumber = newUan;
                employee.ESICNumber = newEsic;
                employee.UpdatedAt = now;
            }

            if (becomesHead && head != null)
            {
                head.IsActive = false;
                head.UpdatedAt = now;
                await _db.SaveChangesAsync();
            }

            var salary = new SalaryMaster
            {
                EmployeeId = employee.Id,
                BasicSalary = basicSalary,
                HRA = hra,
                ConveyanceAllowance = conveyanceAllowance,
                WashingAllowance = washingAllowance,
                OtherAllowances = otherAllowances,
                PFApplicable = pfApplicable,
                ESICApplicable = esicApplicable,
                ProfessionalTaxApplicable = professionalTaxApplicable,
                LWFApplicable = lwfApplicable,
                EffectiveFrom = revisionDate,
                IsActive = becomesHead,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.SalaryMasters.Add(salary);
            await _db.SaveChangesAsync();

            WriteAudit("Create", salary.Id,
                $"Salary revision effective {revisionDate:dd-MMM-yyyy} for employee {employee.Id}: " +
                $"gross {basicSalary + hra + conveyanceAllowance + washingAllowance + otherAllowances}",
                "SalaryMaster");
            if (statutoryChanged)
            {
                WriteAudit("Update", employee.Id,
                    "Statutory numbers (PAN/UAN/ESIC) updated from the salary configuration screen.",
                    "Employee");
            }
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            // Payroll already processed from the effective month is not recalculated.
            var affectedPayrolls = await _db.Payrolls.CountAsync(p =>
                p.EmployeeId == employee.Id &&
                (p.Year > revisionDate.Year ||
                 (p.Year == revisionDate.Year && p.Month >= revisionDate.Month)) &&
                (p.Status == "Processed" || p.Status == "Finalized"));

            TempData["Success"] = affectedPayrolls > 0
                ? $"Salary configuration saved. {affectedPayrolls} processed or finalized payroll(s) " +
                  "from the effective month were not recalculated. Reopen and reprocess them to apply " +
                  "this revision; arrears are not calculated automatically."
                : "Salary configuration saved successfully.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    month = todayIst.Month,
                    year = todayIst.Year
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

            var monthEnd = nextMonthStart.AddDays(-1);
            var leaveYearStart = new DateTime(selectedYear, 1, 1);
            var leaveYearEnd = leaveYearStart.AddYears(1);

            var leavePolicy = await _leavePolicy.GetAsync(CurrentCompanyId);
            var annualPaidLeaveEntitlement = LeavePolicyService.GetAnnualEntitlement(
                employee.JoiningDate, leavePolicy, monthEnd);

            var holidays = await WorkCalendar.LoadHolidaysAsync(
                _db, CurrentCompanyId, leaveYearStart, monthEnd);

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

            var days = PayrollDaysCalculator.Calculate(
                employee, monthStart, lastDateToEvaluate,
                attendance, approvedLeaves, approvedLeavesForYear,
                holidays, annualPaidLeaveEntitlement, GetIndiaAttendanceDate, leavePolicy);

            decimal workingDays = days.WorkingDays;
            decimal paidDays = days.PaidDays;
            decimal lop = days.Lop;
            // Days paid in full despite a missing punch (shown to HR as a warning).
            ViewBag.NoOutPunchDays = attendance.Count(a =>
                (a.Status == "No Out-Punch" || a.Status == "No Punch-In") &&
                a.AttendanceDate < GetIndiaAttendanceDate(todayIndia));

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
            if (action != "Preview" && action != "Process")
                return BadRequest("Invalid action.");

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

            var canProcessPayroll = true;



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

            var monthEnd = nextMonthStart.AddDays(-1);
            var leaveYearStart = new DateTime(year, 1, 1);
            var leaveYearEnd = leaveYearStart.AddYears(1);

            var leavePolicy = await _leavePolicy.GetAsync(CurrentCompanyId);
            var annualPaidLeaveEntitlement = LeavePolicyService.GetAnnualEntitlement(
                employee.JoiningDate, leavePolicy, monthEnd);

            var holidays = await WorkCalendar.LoadHolidaysAsync(
                _db, CurrentCompanyId, leaveYearStart, monthEnd);

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

            var days = PayrollDaysCalculator.Calculate(
                employee, monthStart, lastDateToEvaluate,
                attendance, approvedLeaves, approvedLeavesForYear,
                holidays, annualPaidLeaveEntitlement, GetIndiaAttendanceDate, leavePolicy);

            decimal workingDays = days.WorkingDays;
            decimal paidDays = days.PaidDays;
            decimal lop = days.Lop;
            // Days paid in full despite a missing punch (shown to HR as a warning).
            ViewBag.NoOutPunchDays = attendance.Count(a =>
                (a.Status == "No Out-Punch" || a.Status == "No Punch-In") &&
                a.AttendanceDate < GetIndiaAttendanceDate(todayIndia));

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

            existingPayroll.PaidDays =
                paidDays;

            existingPayroll.LOP =
                lop;
            existingPayroll.Status =
                action == "Process"
                    ? "Processed"
                    : (existingPayroll.Status ?? "Draft");

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

                _db.ChangeTracker.Clear();   // preview must never persist anything

                return View();
            }

            if (netSalary < 0)
            {
                TempData["Error"] =
                    $"Net salary would be negative ({netSalary:N2}). Reduce the deductions before processing.";

                return RedirectToAction(
                    nameof(Process),
                    new { employeeId, month, year });
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
            WriteAudit("Process", existingPayroll.Id,
            $"Processed payroll {month:00}/{year} for employee {employee.Id}: gross {earnedGross}, net {netSalary}, LOP {lop}");
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

            var canFinalizePayroll = true;

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
            WriteAudit("Finalize", payroll.Id,
            $"Finalized payroll {month:00}/{year} for employee {employee.Id}: net {payroll.NetSalary}");
            await _db.SaveChangesAsync();

            TempData["Success"] =
                "Payroll finalized successfully.";

            return RedirectToAction(
                nameof(Index),
                new
                {
                    month,
                    year
                }); }

            // ---------------------------------------------------------
            // REOPEN FINALIZED PAYROLL (correction path)
            // ---------------------------------------------------------
            [Authorize(Roles = "HR")]
            [HttpPost]
            [ValidateAntiForgeryToken]
            public async Task<IActionResult> Reopen(int employeeId, int month, int year, string? reason)
            {
                if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
                {
                    TempData["Error"] = "A reason (at least 5 characters) is required to reopen a finalized payroll.";
                    return RedirectToAction(nameof(Index), new { month, year });
                }

                var employee = await _db.Employees
                    .Include(e => e.User)
                    .FirstOrDefaultAsync(e =>
                        e.Id == employeeId &&
                        e.User.CompanyId == CurrentCompanyId &&
                        e.User.Role == UserRole.Employee);

                if (employee == null) return NotFound();

                var payroll = await _db.Payrolls.FirstOrDefaultAsync(p =>
                    p.EmployeeId == employee.Id && p.Month == month && p.Year == year);

                if (payroll == null || payroll.Status != "Finalized")
                {
                    TempData["Error"] = "Only a finalized payroll can be reopened.";
                    return RedirectToAction(nameof(Index), new { month, year });
                }

                payroll.Status = "Processed";
                payroll.UpdatedAt = DateTime.UtcNow;

                WriteAudit("Reopen", payroll.Id,
                    $"Reopened payroll {month:00}/{year} for employee {employee.Id}. Reason: {reason.Trim()}");

                await _db.SaveChangesAsync();

                TempData["Success"] = "Payroll reopened. Reprocess it, then finalize again.";
                return RedirectToAction(nameof(Index), new { month, year });
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