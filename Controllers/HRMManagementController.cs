using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Data;
using ExpenseManagement.Models;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Management")]
    public class HRMManagementController : Controller
    {
        private readonly AppDbContext _db;

        public HRMManagementController(AppDbContext db)
        {
            _db = db;
        }

        // GET: /HRMManagement/Dashboard
        [HttpGet]
        public async Task<IActionResult> Dashboard(int? companyId)
        {
            // Management can view either company.
            // If no company is selected, default to Hitech for now.
            companyId ??= 1;

            // Validate that the selected company actually exists.
            var selectedCompany = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);

            if (selectedCompany == null)
                return NotFound();

            // Load all companies for the company switcher.
            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            // Employees belonging ONLY to the selected company.
            var employees = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.Role == UserRole.Employee &&
                    u.CompanyId == companyId.Value)
                .ToListAsync();

            // HR users belonging ONLY to the selected company.
            var hrUsers = await _db.Users
                .AsNoTracking()
                .Where(u =>
                    u.Role == UserRole.HR &&
                    u.CompanyId == companyId.Value)
                .ToListAsync();

            ViewBag.SelectedCompanyId = companyId.Value;
            ViewBag.SelectedCompanyName = selectedCompany.Name;

            ViewBag.Companies = companies;

            ViewBag.TotalEmployees = employees.Count;
            ViewBag.TotalHR = hrUsers.Count;

            // These modules have not been implemented yet.
            // Keep them as placeholders rather than showing fake data.
            ViewBag.PresentToday = null;
            ViewBag.OnLeave = null;
            ViewBag.PendingRequests = null;

            return View("~/Views/HR/ManagementDashboard.cshtml");
        }


        // ============================================================
        // MANAGEMENT - EMPLOYEE OVERVIEW
        // READ ONLY
        // ============================================================

        // GET: /HRMManagement/Employees
        [HttpGet]
        public async Task<IActionResult> Employees(int? companyId)
        {
            // Management can view either company.
            // If no company is selected, default to Hitech for now.
            companyId ??= 1;

            // Validate selected company.
            var selectedCompany = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);

            if (selectedCompany == null)
                return NotFound();

            // Load companies for the company switcher.
            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            // Load HRM employees belonging ONLY to the selected company.
            var employees = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Include(e => e.ReportingManager)
                    .ThenInclude(r => r!.User)
                .Where(e =>
                    e.User.CompanyId == companyId.Value &&
                    e.User.Role == UserRole.Employee)
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            ViewBag.SelectedCompanyId = companyId.Value;
            ViewBag.SelectedCompanyName = selectedCompany.Name;
            ViewBag.Companies = companies;

            ViewBag.TotalEmployees = employees.Count;
            ViewBag.ActiveEmployees = employees.Count(
                e => e.EmploymentStatus == "Active");

            ViewBag.InactiveEmployees = employees.Count(
                e => e.EmploymentStatus == "Inactive");

            return View("~/Views/HR/ManagementEmployees.cshtml", employees);
        }

        // ============================================================
        // MANAGEMENT - ATTENDANCE OVERVIEW
        // READ ONLY
        // ============================================================

        // GET: /HRMManagement/Attendance
        [HttpGet]
        public async Task<IActionResult> Attendance(
            int? companyId,
            DateTime? date)
        {
            // Management can view either company.
            // If no company is selected, default to Hitech for now.
            companyId ??= 1;

            // Calendar date displayed to management in India.
            var selectedDate = date.HasValue
                ? date.Value.Date
                : DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            // Convert India calendar date to the same database
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

            // Validate selected company.
            var selectedCompany = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);

            if (selectedCompany == null)
                return NotFound();

            // Load companies for the company switcher.
            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            // Load employees belonging ONLY to the selected company.
            var employees = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Include(e => e.ReportingManager)
                    .ThenInclude(r => r!.User)
                .Where(e =>
                    e.User.CompanyId == companyId.Value &&
                    e.User.Role == UserRole.Employee)
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            // Load ONLY existing attendance records.
            // Viewing a date must never create a new attendance row.
            var attendanceRecords = await _db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.AttendanceDate == attendanceDate &&
                    a.Employee.User.CompanyId == companyId.Value)
                .ToListAsync();

            var attendanceByEmployee = attendanceRecords
                .ToDictionary(a => a.EmployeeId);

            ViewBag.SelectedCompanyId = companyId.Value;
            ViewBag.SelectedCompanyName = selectedCompany.Name;
            ViewBag.Companies = companies;

            ViewBag.AttendanceDate = selectedDate;
            ViewBag.AttendanceRecords = attendanceByEmployee;

            ViewBag.TotalEmployees = employees.Count;
            ViewBag.PresentEmployees = attendanceRecords.Count(
                a => a.Status == "Present");

            ViewBag.HalfDayEmployees = attendanceRecords.Count(
                a => a.Status == "Half Day");

            ViewBag.AbsentEmployees = attendanceRecords.Count(
                a => a.Status == "Absent");

            return View("~/Views/HR/ManagementAttendance.cshtml", employees);
        }

        // ============================================================
        // MANAGEMENT - PAYROLL OVERVIEW
        // READ ONLY
        // ============================================================

        // GET: /HRMManagement/Payroll
        [HttpGet]
        public async Task<IActionResult> Payroll(
            int? companyId,
            int? month,
            int? year)
        {
            // Management can view either company.
            companyId ??= 1;

            var todayIndia = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30);

            var selectedMonth = month ?? todayIndia.Month;
            var selectedYear = year ?? todayIndia.Year;

            // Validate month/year.
            if (selectedMonth < 1 || selectedMonth > 12)
                selectedMonth = todayIndia.Month;

            if (selectedYear < 2000 || selectedYear > 2100)
                selectedYear = todayIndia.Year;

            // ------------------------------------------------------------
            // COMPANY
            // ------------------------------------------------------------

            var selectedCompany = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId.Value);

            if (selectedCompany == null)
                return NotFound();

            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            // ------------------------------------------------------------
            // EMPLOYEES
            // ------------------------------------------------------------

            var employees = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == companyId.Value &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive")
                .OrderBy(e => e.EmployeeCode)
                .ToListAsync();

            // ------------------------------------------------------------
            // SELECTED MONTH
            // ------------------------------------------------------------

            var monthStart = new DateTime(
                selectedYear,
                selectedMonth,
                1);

            var nextMonthStart = monthStart.AddMonths(1);

            // ------------------------------------------------------------
            // PAYROLL
            // ------------------------------------------------------------

            var payrolls = await _db.Payrolls
                .AsNoTracking()
                .Where(p =>
                    p.Employee.User.CompanyId == companyId.Value &&
                    p.Month == selectedMonth &&
                    p.Year == selectedYear)
                .ToListAsync();

            // ------------------------------------------------------------
            // SALARY STRUCTURES
            // ------------------------------------------------------------

            var salaryMasters = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s =>
                    s.Employee.User.CompanyId == companyId.Value &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .ToListAsync();

            // ------------------------------------------------------------
            // BUILD MANAGEMENT PAYROLL ROWS
            // ------------------------------------------------------------

            var rows = employees.Select(employee =>
            {
                var salary = salaryMasters
                    .FirstOrDefault(s =>
                        s.EmployeeId == employee.Id);

                var payroll = payrolls
                    .FirstOrDefault(p =>
                        p.EmployeeId == employee.Id);

                return new
                {
                    Employee = employee,
                    Salary = salary,
                    Payroll = payroll
                };
            }).ToList();

            // ------------------------------------------------------------
            // SUMMARY
            // ------------------------------------------------------------

            var payrollRows = rows
                .Where(r => r.Payroll != null)
                .ToList();

            ViewBag.SelectedCompanyId = companyId.Value;
            ViewBag.SelectedCompanyName = selectedCompany.Name;
            ViewBag.Companies = companies;

            ViewBag.Month = selectedMonth;
            ViewBag.Year = selectedYear;

            ViewBag.Rows = rows;

            ViewBag.TotalEmployees = rows.Count;

            ViewBag.SalaryConfigured =
                rows.Count(r => r.Salary != null);

            ViewBag.PendingPayroll =
                rows.Count(r => r.Payroll == null);

            ViewBag.DraftPayroll =
                payrollRows.Count(r =>
                    r.Payroll!.Status == "Draft");

            ViewBag.ProcessedPayroll =
                payrollRows.Count(r =>
                    r.Payroll!.Status == "Processed");

            ViewBag.FinalizedPayroll =
                payrollRows.Count(r =>
                    r.Payroll!.Status == "Finalized");

            ViewBag.TotalGross =
                payrollRows.Sum(r =>
                    r.Payroll!.GrossSalary);

            ViewBag.TotalDeductions =
                payrollRows.Sum(r =>
                    r.Payroll!.TotalDeductions);

            ViewBag.TotalNetSalary =
                payrollRows.Sum(r =>
                    r.Payroll!.NetSalary);

            return View("~/Views/Payroll/ManagementPayroll.cshtml");
        }

        // ============================================================
        // MANAGEMENT - PAYROLL DETAILS
        // READ ONLY
        // ============================================================

        // GET: /HRMManagement/PayrollDetails
        [HttpGet]
        public async Task<IActionResult> PayrollDetails(
            int employeeId,
            int month,
            int year,
            int companyId)
        {
            // ---------------------------------------------------------
            // VALIDATE MONTH / YEAR
            // ---------------------------------------------------------

            if (month < 1 || month > 12)
                return BadRequest();

            if (year < 2000 || year > 2100)
                return BadRequest();

            // ---------------------------------------------------------
            // VALIDATE COMPANY
            // ---------------------------------------------------------

            var selectedCompany = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId);

            if (selectedCompany == null)
                return NotFound();

            // ---------------------------------------------------------
            // FIND EMPLOYEE
            // Employee MUST belong to selected company
            // ---------------------------------------------------------

            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == companyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

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
                TempData["Error"] = "Payroll record was not found.";

                return RedirectToAction(
                    nameof(Payroll),
                    new
                    {
                        companyId,
                        month,
                        year
                    });
            }

            // ---------------------------------------------------------
            // FIND SALARY STRUCTURE APPLICABLE TO THIS MONTH
            // ---------------------------------------------------------

            var monthStart = new DateTime(year, month, 1);
            var nextMonthStart = monthStart.AddMonths(1);

            var salary = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s =>
                    s.EmployeeId == employee.Id &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .FirstOrDefaultAsync();

            // ---------------------------------------------------------
            // PASS DATA TO READ-ONLY VIEW
            // ---------------------------------------------------------

            ViewBag.Employee = employee;
            ViewBag.Payroll = payroll;
            ViewBag.Salary = salary;

            ViewBag.Month = month;
            ViewBag.Year = year;

            ViewBag.SelectedCompanyId = companyId;
            ViewBag.SelectedCompanyName = selectedCompany.Name;

            return View("~/Views/Payroll/ManagementPayrollDetails.cshtml");
        }

        // ============================================================
        // MANAGEMENT - PAYSLIP
        // READ ONLY
        // Official payslip only for finalized payroll
        // ============================================================

        // GET: /HRMManagement/PayrollPayslip
        [HttpGet]
        public async Task<IActionResult> PayrollPayslip(
            int employeeId,
            int month,
            int year,
            int companyId)
        {
            // ---------------------------------------------------------
            // VALIDATE MONTH / YEAR
            // ---------------------------------------------------------

            if (month < 1 || month > 12)
                return BadRequest();

            if (year < 2000 || year > 2100)
                return BadRequest();

            // ---------------------------------------------------------
            // VALIDATE COMPANY
            // ---------------------------------------------------------

            var selectedCompany = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId);

            if (selectedCompany == null)
                return NotFound();

            // ---------------------------------------------------------
            // FIND EMPLOYEE
            // Employee MUST belong to selected company
            // ---------------------------------------------------------

            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                    .ThenInclude(u => u.Company)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == companyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

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
                TempData["Error"] = "Payroll record was not found.";

                return RedirectToAction(
                    nameof(Payroll),
                    new
                    {
                        companyId,
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
                    "Payslip can only be generated after payroll has been finalized.";

                return RedirectToAction(
                    nameof(Payroll),
                    new
                    {
                        companyId,
                        month,
                        year
                    });
            }

            // ---------------------------------------------------------
            // FIND SALARY STRUCTURE
            // ---------------------------------------------------------

            var monthStart = new DateTime(year, month, 1);
            var nextMonthStart = monthStart.AddMonths(1);

            var salary = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s =>
                    s.EmployeeId == employee.Id &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .FirstOrDefaultAsync();

            // ---------------------------------------------------------
            // PASS DATA TO EXISTING PAYSLIP VIEW
            // ---------------------------------------------------------

            ViewBag.Employee = employee;
            ViewBag.Payroll = payroll;
            ViewBag.Salary = salary;

            ViewBag.Month = month;
            ViewBag.Year = year;

            // Management opens the standard payslip in a read-only popup.
            // Hide navigation/print actions for Management.
            ViewBag.IsManagementPayslip = true;
            return View("~/Views/Payroll/Payslip.cshtml");
        }

        // ============================================================
        // MANAGEMENT - PAYROLL DETAILS DATA
        // READ ONLY / AJAX
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> PayrollDetailsData(
            int employeeId,
            int month,
            int year,
            int companyId)
        {
            if (month < 1 || month > 12)
                return BadRequest();

            if (year < 2000 || year > 2100)
                return BadRequest();

            // ---------------------------------------------------------
            // VALIDATE COMPANY
            // ---------------------------------------------------------

            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == companyId);

            if (company == null)
                return NotFound();

            // ---------------------------------------------------------
            // VALIDATE EMPLOYEE BELONGS TO SELECTED COMPANY
            // ---------------------------------------------------------

            var employee = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == companyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            // ---------------------------------------------------------
            // FIND PAYROLL
            // ---------------------------------------------------------

            var payroll = await _db.Payrolls
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.EmployeeId == employeeId &&
                    p.Month == month &&
                    p.Year == year);

            if (payroll == null)
                return NotFound();

            // ---------------------------------------------------------
            // FIND APPLICABLE SALARY
            // ---------------------------------------------------------

            var monthStart = new DateTime(year, month, 1);
            var nextMonthStart = monthStart.AddMonths(1);

            var salary = await _db.SalaryMasters
                .AsNoTracking()
                .Where(s =>
                    s.EmployeeId == employeeId &&
                    s.EffectiveFrom < nextMonthStart)
                .OrderByDescending(s => s.EffectiveFrom)
                .FirstOrDefaultAsync();

            return Json(new
            {
                employee = new
                {
                    id = employee.Id,
                    name = employee.User.FullName,
                    code = employee.EmployeeCode,
                    department = employee.User.Department,
                    designation = employee.Designation,
                    joiningDate = employee.JoiningDate?.ToString("dd-MM-yyyy")
                },

                company = company.Name,

                payroll = new
                {
                    month,
                    year,
                    status = payroll.Status,

                    workingDays = payroll.WorkingDays,
                    payDays = payroll.PaidDays,
                    paidDays = payroll.PaidDays,
                    lop = payroll.LOP,

                    basicSalary = payroll.BasicSalary,
                    hra = payroll.HRA,
                    conveyanceAllowance = payroll.ConveyanceAllowance,
                    washingAllowance = payroll.WashingAllowance,
                    otherAllowances = payroll.OtherAllowances,

                    grossSalary = payroll.GrossSalary,

                    pf = payroll.PF,
                    esi = payroll.ESI,
                    professionalTax = payroll.ProfessionalTax,
                    lwf = payroll.LWF,
                    otherDeductions = payroll.OtherDeductions,

                    totalDeductions = payroll.TotalDeductions,
                    netSalary = payroll.NetSalary,

                    remarks = payroll.Remarks
                }
            });
        }

        // ============================================================
        // MANAGEMENT - ASSETS
        // READ ONLY
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Assets(
            int? companyId,
            int? assetId)
        {
            // Management can view either company.
            // This follows the same company-selector pattern
            // already used by the Management controller.

            companyId ??= 1;

            var selectedCompany =
                await _db.Companies
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        c => c.Id == companyId.Value);

            if (selectedCompany == null)
                return NotFound();

            // Company selector.
            var companies =
                await _db.Companies
                    .AsNoTracking()
                    .OrderBy(c => c.Name)
                    .ToListAsync();

            // Assets ONLY from selected company.
            var assets =
                await _db.Assets
                    .AsNoTracking()
                    .Include(a => a.Company)
                    .Include(a => a.CurrentEmployee)
                        .ThenInclude(e => e!.User)
                    .Where(a =>
                        a.CompanyId == companyId.Value)
                    .OrderBy(a => a.AssetName)
                    .ThenBy(a => a.AssetCode)
                    .ToListAsync();

            ViewBag.SelectedCompanyId =
                companyId.Value;

            ViewBag.SelectedCompanyName =
                selectedCompany.Name;

            ViewBag.Companies =
                companies;

            ViewBag.TotalAssets =
                assets.Count;

            ViewBag.AvailableAssets =
                assets.Count(
                    a => a.Status == "Available");

            ViewBag.AssignedAssets =
                assets.Count(
                    a => a.Status == "Assigned");

            ViewBag.MaintenanceAssets =
                assets.Count(
                    a => a.Status == "Under Maintenance");


            // ========================================================
            // ASSET DETAILS
            // ========================================================

            if (assetId.HasValue)
            {
                var asset =
                    assets.FirstOrDefault(
                        a => a.Id == assetId.Value);

                // This also prevents Management from requesting
                // an asset belonging to another company.
                if (asset == null)
                    return NotFound();

                ViewBag.AssetPage = "Details";

                ViewBag.Asset = asset;

                ViewBag.History =
                    await _db.AssetHistories
                        .AsNoTracking()
                        .Include(h => h.Employee)
                            .ThenInclude(e => e!.User)
                        .Include(h => h.PerformedByUser)
                        .Where(h =>
                            h.AssetId == asset.Id)
                        .OrderByDescending(h => h.ActionDate)
                        .ThenByDescending(h => h.Id)
                        .ToListAsync();
            }
            else
            {
                ViewBag.AssetPage = "Index";
            }

            return View(
                "~/Views/Asset/AssetManagement.cshtml",
                assets);
        }


    }
}