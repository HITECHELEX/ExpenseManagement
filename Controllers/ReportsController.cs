using ClosedXML.Excel;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Data;
using ExpenseManagement.Models;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "HR,Management")]
    public class ReportsController : Controller
    {
        private readonly AppDbContext _db;

        public ReportsController(AppDbContext db)
        {
            _db = db;
        }

        // ============================================================
        // CURRENT COMPANY
        // ============================================================

        private int? CurrentCompanyId
        {
            get
            {
                var companyId = User.FindFirst("CompanyId")?.Value;

                if (int.TryParse(companyId, out var id))
                    return id;

                return null;
            }
        }

        // ============================================================
        // REPORTS DASHBOARD
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Index(int? companyId)
        {
            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            if (!companies.Any())
                return NotFound("No companies are configured.");

            int selectedCompanyId;

            if (User.IsInRole("HR"))
            {
                var hrCompanyId = CurrentCompanyId;

                if (!hrCompanyId.HasValue)
                    return Forbid();

                var hrCompanyExists = companies.Any(
                    c => c.Id == hrCompanyId.Value);

                if (!hrCompanyExists)
                    return Forbid();

                selectedCompanyId = hrCompanyId.Value;
            }
            else if (User.IsInRole("Management"))
            {
                selectedCompanyId =
                    companyId ??
                    companies.First().Id;

                var selectedCompanyExists = companies.Any(
                    c => c.Id == selectedCompanyId);

                if (!selectedCompanyExists)
                    return NotFound();
            }
            else
            {
                return Forbid();
            }

            var selectedCompany = companies.First(
                c => c.Id == selectedCompanyId);

            ViewBag.SelectedCompanyId = selectedCompany.Id;
            ViewBag.SelectedCompanyName = selectedCompany.Name;
            ViewBag.Companies = companies;

            return View("~/Views/Reports/Index.cshtml");
        }

        // ============================================================
        // ATTENDANCE REPORT
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> AttendanceReport(
            int? companyId,
            int? month,
            int? year,
            string? department,
            int? employeeId,
            string? status,
            string? mode)
        {
            // --------------------------------------------------------
            // Resolve company
            // --------------------------------------------------------

            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            if (!companies.Any())
                return NotFound("No companies are configured.");

            int selectedCompanyId;

            if (User.IsInRole("HR"))
            {
                var hrCompanyId = CurrentCompanyId;

                if (!hrCompanyId.HasValue)
                    return Forbid();

                if (!companies.Any(c => c.Id == hrCompanyId.Value))
                    return Forbid();

                selectedCompanyId = hrCompanyId.Value;
            }
            else if (User.IsInRole("Management"))
            {
                selectedCompanyId =
                    companyId ??
                    companies.First().Id;

                if (!companies.Any(c => c.Id == selectedCompanyId))
                    return NotFound();
            }
            else
            {
                return Forbid();
            }

            // --------------------------------------------------------
            // India current date
            // --------------------------------------------------------

            var indiaToday = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30)
                .Date;

            var selectedMonth = month ?? indiaToday.Month;
            var selectedYear = year ?? indiaToday.Year;

            if (selectedMonth < 1 || selectedMonth > 12)
                selectedMonth = indiaToday.Month;

            if (selectedYear < 2000 || selectedYear > 2100)
                selectedYear = indiaToday.Year;

            var monthStart = new DateTime(
                selectedYear,
                selectedMonth,
                1);

            var nextMonthStart = monthStart.AddMonths(1);

            // AttendanceDate is stored using the existing
            // IST-midnight -> UTC representation.
            var monthStartUtc =
                GetIndiaAttendanceDate(monthStart);

            var nextMonthStartUtc =
                GetIndiaAttendanceDate(nextMonthStart);

            // --------------------------------------------------------
            // Employees
            //
            // Same company and employee-role rules used by the
            // existing Attendance module.
            // --------------------------------------------------------

            var employeesQuery = _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == selectedCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive" &&
                    (!e.JoiningDate.HasValue ||
                     e.JoiningDate.Value.Date <= monthStart.Date));

            if (!string.IsNullOrWhiteSpace(department))
            {
                employeesQuery = employeesQuery.Where(e =>
                    e.User.Department == department);
            }

            if (employeeId.HasValue)
            {
                employeesQuery = employeesQuery.Where(e =>
                    e.Id == employeeId.Value);
            }

            var employees = await employeesQuery
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            // --------------------------------------------------------
            // Attendance records
            // --------------------------------------------------------

            var attendanceQuery = _db.Attendances
                .AsNoTracking()
                .Include(a => a.Employee)
                    .ThenInclude(e => e.User)
                .Where(a =>
                    a.AttendanceDate >= monthStartUtc &&
                    a.AttendanceDate < nextMonthStartUtc &&
                    a.Employee.User.CompanyId == selectedCompanyId);

            if (employeeId.HasValue)
            {
                attendanceQuery = attendanceQuery.Where(a =>
                    a.EmployeeId == employeeId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                attendanceQuery = attendanceQuery.Where(a =>
                    a.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(mode))
            {
                attendanceQuery = attendanceQuery.Where(a =>
                    a.AttendanceMode == mode);
            }

            var attendanceRecords = await attendanceQuery
                .OrderBy(a => a.AttendanceDate)
                .ThenBy(a => a.Employee.User.FullName)
                .ToListAsync();

            // --------------------------------------------------------
            // Approved leaves
            // --------------------------------------------------------

            var approvedLeaves = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.Employee.User.CompanyId == selectedCompanyId &&
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate.Date < nextMonthStart.Date &&
                    l.ToDate.Date >= monthStart.Date)
                .ToListAsync();

            // --------------------------------------------------------
            // Department list
            // --------------------------------------------------------

            var departments = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == selectedCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    !string.IsNullOrWhiteSpace(e.User.Department))
                .Select(e => e.User.Department!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            // --------------------------------------------------------
            // Employee filter list
            //
            // Use all employees for the selected company.
            // --------------------------------------------------------

            var employeeFilterList = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == selectedCompanyId &&
                    e.User.Role == UserRole.Employee)
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            // ========================================================
            // BUILD EMPLOYEE-WISE SUMMARY
            // ========================================================

            var summaryRows = new List<AttendanceReportRow>();

            foreach (var employee in employees)
            {
                var employeeAttendance = attendanceRecords
                    .Where(a => a.EmployeeId == employee.Id)
                    .ToList();

                var employeeLeaves = approvedLeaves
                    .Where(l => l.EmployeeId == employee.Id)
                    .ToList();

                int workingDays = 0;
                int presentCount = 0;
                int halfDayCount = 0;
                int absentCount = 0;
                int leaveCount = 0;
                int noPunchInCount = 0;
                int noOutPunchCount = 0;

                int lateCount = employeeAttendance.Count(a =>
                    a.IsLate &&
                    a.LateReviewStatus != "Approved");

                decimal totalWorkingHours = 0;

                // ----------------------------------------------------
                // Count working days
                // ----------------------------------------------------

                for (
                    var day = monthStart;
                    day < nextMonthStart;
                    day = day.AddDays(1))
                {
                    // Employee had not joined yet.
                    if (employee.JoiningDate.HasValue &&
                        day.Date < employee.JoiningDate.Value.Date)
                    {
                        continue;
                    }

                    if (!IsWorkingDay(day))
                        continue;

                    workingDays++;
                }

                // ----------------------------------------------------
                // Process actual attendance records
                // ----------------------------------------------------

                foreach (var attendance in employeeAttendance)
                {
                    if (attendance.Status == "On Leave")
                    {
                        leaveCount++;
                        continue;
                    }

                    if (attendance.WorkingHours.HasValue)
                    {
                        totalWorkingHours +=
                            attendance.WorkingHours.Value;
                    }

                    // Both punches
                    if (attendance.CheckIn.HasValue &&
                        attendance.CheckOut.HasValue)
                    {
                        if (attendance.Status == "Half Day")
                            halfDayCount++;
                        else
                            presentCount++;

                        continue;
                    }

                    // No punch-in but out-punch exists
                    if (!attendance.CheckIn.HasValue &&
                        attendance.CheckOut.HasValue)
                    {
                        presentCount++;
                        noPunchInCount++;
                        continue;
                    }

                    // Punch-in but no out-punch
                    if (attendance.CheckIn.HasValue &&
                        !attendance.CheckOut.HasValue)
                    {
                        presentCount++;
                        noOutPunchCount++;
                        continue;
                    }

                    // No punches
                    if (!attendance.CheckIn.HasValue &&
                        !attendance.CheckOut.HasValue)
                    {
                        absentCount++;
                    }
                }

                // ----------------------------------------------------
                // For past/current months, missing working-day
                // attendance is treated as absent unless approved leave
                // exists.
                //
                // This follows the existing Attendance module logic.
                // ----------------------------------------------------

                DateTime? lastDateToEvaluate = null;

                if (monthStart.Date < new DateTime(
                        indiaToday.Year,
                        indiaToday.Month,
                        1))
                {
                    // Past month
                    lastDateToEvaluate =
                        nextMonthStart.AddDays(-1);
                }
                else if (
                    selectedYear == indiaToday.Year &&
                    selectedMonth == indiaToday.Month)
                {
                    // Current month — evaluate only up to today.
                    lastDateToEvaluate = indiaToday;
                }

                if (lastDateToEvaluate.HasValue)
                {
                    for (
                        var day = monthStart;
                        day <= lastDateToEvaluate.Value;
                        day = day.AddDays(1))
                    {
                        if (employee.JoiningDate.HasValue &&
                            day.Date < employee.JoiningDate.Value.Date)
                        {
                            continue;
                        }

                        if (!IsWorkingDay(day))
                            continue;

                        var attendanceDate =
                            GetIndiaAttendanceDate(day);

                        var dayAttendance =
                            employeeAttendance.FirstOrDefault(a =>
                                a.AttendanceDate == attendanceDate);

                        if (dayAttendance != null)
                            continue;

                        var hasApprovedLeave =
                            employeeLeaves.Any(l =>
                                l.FromDate.Date <= day.Date &&
                                l.ToDate.Date >= day.Date);

                        if (hasApprovedLeave)
                            leaveCount++;
                        else
                            absentCount++;
                    }
                }

                // ----------------------------------------------------
                // Attendance percentage
                //
                // Half Day = 0.5 attendance day
                // Leave is not counted as present.
                // ----------------------------------------------------

                decimal attendancePercentage = 0;

                if (workingDays > 0)
                {
                    var attendedDays =
                        presentCount +
                        (halfDayCount * 0.5m);

                    attendancePercentage =
                        Math.Round(
                            (attendedDays / workingDays) * 100m,
                            2);
                }

                summaryRows.Add(
                    new AttendanceReportRow
                    {
                        EmployeeId = employee.Id,
                        EmployeeCode = employee.EmployeeCode,
                        EmployeeName = employee.User.FullName,
                        Department = employee.User.Department,
                        Designation = employee.Designation,
                        JoiningDate = employee.JoiningDate,

                        WorkingDays = workingDays,
                        Present = presentCount,
                        HalfDay = halfDayCount,
                        Absent = absentCount,
                        Leave = leaveCount,

                        Late = lateCount,
                        NoPunchIn = noPunchInCount,
                        NoOutPunch = noOutPunchCount,

                        TotalWorkingHours =
                            Math.Round(totalWorkingHours, 2),

                        AttendancePercentage =
                            attendancePercentage
                    });
            }

            // ========================================================
            // DAILY DETAILS
            // ========================================================

            var dailyRows = attendanceRecords
                .Select(a => new AttendanceDailyRow
                {
                    AttendanceDate =
                        ConvertAttendanceDateToIndiaDate(
                            a.AttendanceDate),

                    EmployeeCode =
                        a.Employee?.EmployeeCode ?? "",

                    EmployeeName =
                        a.Employee?.User?.FullName ?? "",

                    Department =
                        a.Employee?.User?.Department,

                    Status =
                        a.Status,

                    CheckIn =
                        ConvertUtcToIndiaTime(a.CheckIn),

                    CheckInLocationName =
                        a.CheckInLocationName,

                    CheckOut =
                        ConvertUtcToIndiaTime(a.CheckOut),

                    CheckOutLocationName =
                        a.CheckOutLocationName,

                    WorkingHours =
                        a.WorkingHours,

                    LateMinutes =
                        a.LateMinutes,

                    EarlyLeaveMinutes =
                        a.EarlyLeaveMinutes,

                    AttendanceMode =
                        a.AttendanceMode,

                    LateReviewStatus =
                        a.LateReviewStatus
                })
                .OrderBy(x => x.AttendanceDate)
                .ThenBy(x => x.EmployeeName)
                .ToList();

            // ========================================================
            // LATE ATTENDANCE
            // ========================================================

            var lateRows = attendanceRecords
                .Where(a =>
                    a.IsLate &&
                    a.LateReviewStatus != "Approved")
                .Select(a => new AttendanceLateRow
                {
                    AttendanceDate =
                        ConvertAttendanceDateToIndiaDate(
                            a.AttendanceDate),

                    EmployeeCode =
                        a.Employee?.EmployeeCode ?? "",

                    EmployeeName =
                        a.Employee?.User?.FullName ?? "",

                    Department =
                        a.Employee?.User?.Department,

                    CheckIn =
                        ConvertUtcToIndiaTime(a.CheckIn),

                    LateMinutes =
                        a.LateMinutes ?? 0,

                    LateMarkNumber =
                        a.LateMarkNumber,

                    ReviewStatus =
                        a.LateReviewStatus ?? "Not Required",

                    Reason =
                        a.LateReason
                })
                .OrderBy(x => x.AttendanceDate)
                .ThenBy(x => x.EmployeeName)
                .ToList();

            // ========================================================
            // OVERALL SUMMARY
            // ========================================================

            var overallWorkingDays =
                summaryRows.Sum(x => x.WorkingDays);

            var overallPresent =
                summaryRows.Sum(x => x.Present);

            var overallHalfDay =
                summaryRows.Sum(x => x.HalfDay);

            var overallAbsent =
                summaryRows.Sum(x => x.Absent);

            var overallLeave =
                summaryRows.Sum(x => x.Leave);

            var overallLate =
                summaryRows.Sum(x => x.Late);

            var overallNoPunchIn =
                summaryRows.Sum(x => x.NoPunchIn);

            var overallNoOutPunch =
                summaryRows.Sum(x => x.NoOutPunch);

            var overallWorkingHours =
                summaryRows.Sum(x => x.TotalWorkingHours);

            // ========================================================
            // VIEWBAGS
            // ========================================================

            var selectedCompany =
                companies.First(c =>
                    c.Id == selectedCompanyId);

            ViewBag.Companies = companies;
            ViewBag.SelectedCompanyId =
                selectedCompanyId;
            ViewBag.SelectedCompanyName =
                selectedCompany.Name;

            ViewBag.Month = selectedMonth;
            ViewBag.Year = selectedYear;

            ViewBag.SelectedDepartment =
                department;

            ViewBag.SelectedEmployeeId =
                employeeId;

            ViewBag.SelectedStatus =
                status;

            ViewBag.SelectedMode =
                mode;

            ViewBag.Departments =
                departments;

            ViewBag.EmployeeFilterList =
                employeeFilterList;

            ViewBag.SummaryRows =
                summaryRows;

            ViewBag.DailyRows =
                dailyRows;

            ViewBag.LateRows =
                lateRows;

            ViewBag.OverallWorkingDays =
                overallWorkingDays;

            ViewBag.OverallPresent =
                overallPresent;

            ViewBag.OverallHalfDay =
                overallHalfDay;

            ViewBag.OverallAbsent =
                overallAbsent;

            ViewBag.OverallLeave =
                overallLeave;

            ViewBag.OverallLate =
                overallLate;

            ViewBag.OverallNoPunchIn =
                overallNoPunchIn;

            ViewBag.OverallNoOutPunch =
                overallNoOutPunch;

            ViewBag.OverallWorkingHours =
                Math.Round(overallWorkingHours, 2);

            return View(
                "~/Views/Reports/AttendanceReport.cshtml");
        }

        // ============================================================
        // GENERATE ATTENDANCE EXCEL REPORT
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> GenerateAttendanceExcel(
            int? companyId,
            int? month,
            int? year,
            string? department,
            int? employeeId,
            string? status,
            string? mode)
        {
            // --------------------------------------------------------
            // RESOLVE COMPANY
            // --------------------------------------------------------

            var companies = await _db.Companies
                .AsNoTracking()
                .OrderBy(c => c.Name)
                .ToListAsync();

            if (!companies.Any())
                return NotFound("No companies are configured.");

            int selectedCompanyId;

            if (User.IsInRole("HR"))
            {
                var hrCompanyId = CurrentCompanyId;

                if (!hrCompanyId.HasValue)
                    return Forbid();

                if (!companies.Any(c => c.Id == hrCompanyId.Value))
                    return Forbid();

                selectedCompanyId = hrCompanyId.Value;
            }
            else if (User.IsInRole("Management"))
            {
                selectedCompanyId =
                    companyId ??
                    companies.First().Id;

                if (!companies.Any(c => c.Id == selectedCompanyId))
                    return NotFound();
            }
            else
            {
                return Forbid();
            }

            // --------------------------------------------------------
            // INDIA CURRENT DATE
            // --------------------------------------------------------

            var indiaToday = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30)
                .Date;

            var selectedMonth = month ?? indiaToday.Month;
            var selectedYear = year ?? indiaToday.Year;

            if (selectedMonth < 1 || selectedMonth > 12)
                selectedMonth = indiaToday.Month;

            if (selectedYear < 2000 || selectedYear > 2100)
                selectedYear = indiaToday.Year;

            var monthStart = new DateTime(
                selectedYear,
                selectedMonth,
                1);

            var nextMonthStart = monthStart.AddMonths(1);

            var daysInMonth =
                DateTime.DaysInMonth(
                    selectedYear,
                    selectedMonth);

            var monthStartUtc =
                GetIndiaAttendanceDate(monthStart);

            var nextMonthStartUtc =
                GetIndiaAttendanceDate(nextMonthStart);

            // --------------------------------------------------------
            // ACTIVE ATTENDANCE TEMPLATE
            // --------------------------------------------------------

            const string attendanceTemplateCode =
                "ATTENDANCE_REPORT";

            var template = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.CompanyId == selectedCompanyId &&
                    d.DocumentKind == "Template" &&
                    d.DocumentType == "Monthly Attendance Summary" &&
                    d.TemplateCode == attendanceTemplateCode &&
                    d.DocumentStatus == "Active");

            if (template == null)
            {
                return NotFound(
                    "No active Monthly Attendance Summary template was found for the selected company.");
            }

            if (string.IsNullOrWhiteSpace(template.FilePath))
            {
                return NotFound(
                    "The attendance template file path is missing.");
            }

            var templatePhysicalPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                template.FilePath);

            if (!System.IO.File.Exists(templatePhysicalPath))
            {
                return NotFound(
                    "The attendance template file could not be found.");
            }

            var templateExtension =
                Path.GetExtension(
                    template.FileName ?? template.FilePath)
                    .ToLowerInvariant();

            if (templateExtension != ".xlsx")
            {
                return BadRequest(
                    "The Attendance Report template must be an XLSX file.");
            }

            // --------------------------------------------------------
            // EMPLOYEES
            // --------------------------------------------------------

            var employeesQuery = _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == selectedCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive" &&
                    (!e.JoiningDate.HasValue ||
                     e.JoiningDate.Value.Date <= monthStart.Date));

            if (!string.IsNullOrWhiteSpace(department))
            {
                employeesQuery = employeesQuery.Where(e =>
                    e.User.Department == department);
            }

            if (employeeId.HasValue)
            {
                employeesQuery = employeesQuery.Where(e =>
                    e.Id == employeeId.Value);
            }

            var employees = await employeesQuery
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            if (!employees.Any())
            {
                return NotFound(
                    "No employees were found for the selected report filters.");
            }

            // --------------------------------------------------------
            // ATTENDANCE
            // --------------------------------------------------------

            var attendanceQuery = _db.Attendances
                .AsNoTracking()
                .Include(a => a.Employee)
                    .ThenInclude(e => e.User)
                .Where(a =>
                    a.AttendanceDate >= monthStartUtc &&
                    a.AttendanceDate < nextMonthStartUtc &&
                    a.Employee.User.CompanyId == selectedCompanyId);

            if (employeeId.HasValue)
            {
                attendanceQuery = attendanceQuery.Where(a =>
                    a.EmployeeId == employeeId.Value);
            }

            /*
             * IMPORTANT:
             * We intentionally do NOT apply status/mode filtering here.
             *
             * The Excel report needs the complete employee/day picture
             * so missing attendance can correctly become Absent,
             * WeeklyOff, Leave, etc.
             *
             * The filters are applied after the daily status is resolved.
             */

            var attendanceRecords = await attendanceQuery
                .OrderBy(a => a.AttendanceDate)
                .ThenBy(a => a.Employee.User.FullName)
                .ToListAsync();

            // --------------------------------------------------------
            // APPROVED LEAVES
            // --------------------------------------------------------

            var approvedLeaves = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.Employee.User.CompanyId == selectedCompanyId &&
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate.Date < nextMonthStart.Date &&
                    l.ToDate.Date >= monthStart.Date)
                .ToListAsync();

            // --------------------------------------------------------
            // COMPANY
            // --------------------------------------------------------

            var selectedCompany = companies.First(
                c => c.Id == selectedCompanyId);

            // --------------------------------------------------------
            // BUILD EXCEL
            // --------------------------------------------------------

            using var workbook =
                new XLWorkbook(templatePhysicalPath);

            var worksheet =
                workbook.Worksheets
                    .FirstOrDefault(ws =>
                        ws.Name == "DailyAttendance_SummaryReport");

            if (worksheet == null)
            {
                worksheet = workbook.Worksheets.First();
            }

            // --------------------------------------------------------
            // HEADER
            // --------------------------------------------------------

            worksheet.Cell("B2").Value =
                "Daily Attendance Report (Summary Report)";

            worksheet.Cell("B4").Value =
                $"{monthStart:MMM dd yyyy}  To  {nextMonthStart.AddDays(-1):MMM dd yyyy}";

            worksheet.Cell("C5").Value =
                selectedCompany.Name;

            worksheet.Cell("J5").Value =
                $"Printed On : {indiaToday:MMM dd yyyy}";

            worksheet.Cell("D8").Value =
                string.IsNullOrWhiteSpace(department)
                    ? "All Departments"
                    : department;

            // --------------------------------------------------------
            // REMOVE SAMPLE EMPLOYEE DATA
            // --------------------------------------------------------

            const int firstEmployeeHeaderRow = 9;
            const int employeeBlockHeight = 35;

            /*
             * The uploaded template contains sample employee blocks.
             *
             * We keep the first block as the formatting source,
             * then remove the sample content from the report area.
             */

            if (worksheet.LastRowUsed() != null)
            {
                var lastUsedRow =
                    worksheet.LastRowUsed().RowNumber();

                if (lastUsedRow >= firstEmployeeHeaderRow)
                {
                    worksheet.Rows(
                        firstEmployeeHeaderRow,
                        lastUsedRow)
                        .Clear(XLClearOptions.Contents);
                }
            }

            // --------------------------------------------------------
            // CREATE EMPLOYEE BLOCKS
            // --------------------------------------------------------

            int currentRow = firstEmployeeHeaderRow;

            foreach (var employee in employees)
            {
                var employeeAttendance =
                    attendanceRecords
                        .Where(a =>
                            a.EmployeeId == employee.Id)
                        .ToList();

                var employeeLeaves =
                    approvedLeaves
                        .Where(l =>
                            l.EmployeeId == employee.Id)
                        .ToList();

                // ----------------------------------------------------
                // EMPLOYEE HEADER
                // ----------------------------------------------------

                worksheet.Cell(currentRow, 2).Value =
                    "Employee Code:";

                worksheet.Cell(currentRow, 4).Value =
                    employee.EmployeeCode;

                worksheet.Cell(currentRow, 5).Value =
                    "Employee Name :";

                worksheet.Cell(currentRow, 8).Value =
                    employee.User.FullName;

                // ----------------------------------------------------
                // DAILY HEADER
                // ----------------------------------------------------

                var dailyHeaderRow =
                    currentRow + 1;

                worksheet.Cell(dailyHeaderRow, 2).Value =
                    "Date";

                worksheet.Cell(dailyHeaderRow, 4).Value =
                    "InTime";

                worksheet.Cell(dailyHeaderRow, 5).Value =
                    "Check-In Location";

                worksheet.Cell(dailyHeaderRow, 6).Value =
                    "OutTime";

                worksheet.Cell(dailyHeaderRow, 7).Value =
                    "Check-Out Location";

                worksheet.Cell(dailyHeaderRow, 8).Value =
                    "Shift";

                worksheet.Cell(dailyHeaderRow, 9).Value =
                    "Total Duration";

                worksheet.Cell(dailyHeaderRow, 10).Value =
                    "Status";

                worksheet.Cell(dailyHeaderRow, 11).Value =
                    "Remarks";

                // ----------------------------------------------------
                // DAILY ROWS
                // ----------------------------------------------------

                var dailyStartRow =
                    dailyHeaderRow + 1;

                decimal totalWorkingHours = 0;

                int presentDays = 0;
                decimal presentDayValue = 0;
                int leaveDays = 0;
                int absentDays = 0;
                int weeklyOffDays = 0;
                int holidayDays = 0;

                for (int dayNumber = 1;
                     dayNumber <= daysInMonth;
                     dayNumber++)
                {
                    var date =
                        new DateTime(
                            selectedYear,
                            selectedMonth,
                            dayNumber);

                    var row =
                        dailyStartRow +
                        dayNumber -
                        1;

                    var attendanceDate =
                        GetIndiaAttendanceDate(date);

                    var attendance =
                        employeeAttendance
                            .FirstOrDefault(a =>
                                a.AttendanceDate ==
                                attendanceDate);

                    var approvedLeave =
                        employeeLeaves.Any(l =>
                            l.FromDate.Date <= date.Date &&
                            l.ToDate.Date >= date.Date);

                    // ------------------------------------------------
                    // DEFAULT VALUES
                    // ------------------------------------------------

                    string dayStatus;
                    string remarks = "";

                    DateTime? checkIn = null;
                    DateTime? checkOut = null;

                    string checkInLocation =
                        attendance?.CheckInLocationName ?? "";

                    string checkOutLocation =
                        attendance?.CheckOutLocationName ?? "";

                    decimal workingHours = 0;

                    string shiftCode =
                        attendance?.ShiftCode ?? "";

                    // ------------------------------------------------
                    // BEFORE JOINING DATE
                    // ------------------------------------------------

                    if (employee.JoiningDate.HasValue &&
                        date.Date <
                        employee.JoiningDate.Value.Date)
                    {
                        dayStatus = "Not Joined";
                    }

                    // ------------------------------------------------
                    // WEEKLY OFF
                    // ------------------------------------------------

                    else if (!IsWorkingDay(date))
                    {
                        dayStatus = "WeeklyOff";
                        weeklyOffDays++;
                    }

                    // ------------------------------------------------
                    // APPROVED LEAVE
                    // ------------------------------------------------

                    else if (approvedLeave)
                    {
                        dayStatus = "Leave";
                        leaveDays++;
                    }

                    // ------------------------------------------------
                    // ACTUAL ATTENDANCE
                    // ------------------------------------------------

                    else if (attendance != null)
                    {
                        checkIn =
                            ConvertUtcToIndiaTime(
                                attendance.CheckIn);

                        checkOut =
                            ConvertUtcToIndiaTime(
                                attendance.CheckOut);

                        workingHours =
                            attendance.WorkingHours ?? 0;

                        totalWorkingHours +=
                            workingHours;

                        dayStatus =
                            attendance.Status ?? "";

                        if (string.IsNullOrWhiteSpace(dayStatus))
                        {
                            if (checkIn.HasValue &&
                                checkOut.HasValue)
                            {
                                dayStatus = "Present";
                            }
                            else if (checkIn.HasValue)
                            {
                                dayStatus =
                                    "Present (No OutPunch)";
                            }
                            else if (checkOut.HasValue)
                            {
                                dayStatus =
                                    "Present (No InPunch)";
                            }
                            else
                            {
                                dayStatus = "Absent";
                            }
                        }

                        if (attendance.Status == "Half Day")
                        {
                            presentDayValue += 0.5m;
                        }
                        else if (
                            checkIn.HasValue ||
                            checkOut.HasValue ||
                            attendance.Status == "Present")
                        {
                            presentDayValue += 1m;
                        }

                        if (attendance.LateMinutes.HasValue &&
                            attendance.LateMinutes.Value > 0)
                        {
                            remarks =
                                $"Late by {attendance.LateMinutes.Value} min";
                        }

                        if (attendance.EarlyLeaveMinutes.HasValue &&
                            attendance.EarlyLeaveMinutes.Value > 0)
                        {
                            if (!string.IsNullOrWhiteSpace(remarks))
                                remarks += "; ";

                            remarks +=
                                $"Early leave {attendance.EarlyLeaveMinutes.Value} min";
                        }

                        if (!string.IsNullOrWhiteSpace(
                                attendance.Remarks))
                        {
                            if (!string.IsNullOrWhiteSpace(remarks))
                                remarks += "; ";

                            remarks += attendance.Remarks;
                        }
                    }

                    // ------------------------------------------------
                    // NO ATTENDANCE RECORD
                    // ------------------------------------------------

                    else
                    {
                        dayStatus = "Absent";
                        absentDays++;
                    }

                    /// ------------------------------------------------
                    // COUNT STATUS
                    // ------------------------------------------------

                    if (dayStatus.Equals(
                            "Half Day",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        presentDayValue += 0.5m;
                    }
                    else if (
                        dayStatus.Contains(
                            "Present",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        presentDayValue += 1m;
                    }
                    else if (
                        dayStatus.Equals(
                            "Leave",
                            StringComparison.OrdinalIgnoreCase) ||
                        dayStatus.Equals(
                            "On Leave",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        // Already counted as leave.
                    }
                    else if (
                        dayStatus.Equals(
                            "Absent",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (attendance != null)
                            absentDays++;
                    }

                    // ------------------------------------------------
                    // WRITE DAILY DATA
                    // ------------------------------------------------

                    worksheet.Cell(row, 2).Value =
                        date;

                    worksheet.Cell(row, 2)
                        .Style.DateFormat.Format =
                        "dd-MMM-yyyy";

                    worksheet.Cell(row, 4).Value =
                        checkIn.HasValue
                            ? checkIn.Value.ToString("HH:mm")
                            : "";

                    worksheet.Cell(row, 5).Value =
                        checkInLocation;

                    worksheet.Cell(row, 6).Value =
                        checkOut.HasValue
                            ? checkOut.Value.ToString("HH:mm")
                            : "";

                    worksheet.Cell(row, 7).Value =
                        checkOutLocation;

                    worksheet.Cell(row, 8).Value =
                        shiftCode;

                    worksheet.Cell(row, 9).Value =
                        FormatWorkingHours(workingHours);

                    worksheet.Cell(row, 10).Value =
                        dayStatus;

                    worksheet.Cell(row, 11).Value =
                        remarks;
                }

                // ----------------------------------------------------
                // EMPLOYEE TOTAL
                // ----------------------------------------------------

                var totalRow =
                    dailyStartRow +
                    daysInMonth;

                worksheet.Cell(totalRow, 2).Value =
                    BuildEmployeeTotalText(
                        totalWorkingHours,
                        presentDayValue,
                        leaveDays,
                        holidayDays,
                        absentDays,
                        weeklyOffDays);

                // ----------------------------------------------------
                // MOVE TO NEXT EMPLOYEE
                // ----------------------------------------------------

                currentRow =
                    totalRow + 2;
            }

            // --------------------------------------------------------
            // REMOVE UNUSED TEMPLATE ROWS / SAMPLE AREA
            // --------------------------------------------------------

            /*
             * We don't need to preserve the original August
             * employee blocks after the dynamically generated
             * employee blocks.
             */

            var generatedLastRow =
                Math.Max(
                    currentRow - 1,
                    firstEmployeeHeaderRow);

            var originalLastRow =
                worksheet.LastRowUsed()?.RowNumber() ?? generatedLastRow;

            if (originalLastRow > generatedLastRow)
            {
                worksheet.Rows(
                    generatedLastRow + 1,
                    originalLastRow)
                    .Delete();
            }

            // --------------------------------------------------------
            // PAGE SETUP
            // --------------------------------------------------------

            worksheet.PageSetup.PageOrientation =
                XLPageOrientation.Landscape;

            worksheet.PageSetup.PaperSize =
                XLPaperSize.A4Paper;

            worksheet.PageSetup.FitToPages(1, 0);

            worksheet.PageSetup.SetRowsToRepeatAtTop(1, 10);

            // --------------------------------------------------------
            // OUTPUT
            // --------------------------------------------------------

            using var outputStream =
                new MemoryStream();

            workbook.SaveAs(outputStream);

            var fileBytes =
                outputStream.ToArray();

            var companySafeName =
                MakeSafeFileName(
                    selectedCompany.Name);

            var monthName =
                CultureInfo.InvariantCulture
                    .DateTimeFormat
                    .GetMonthName(selectedMonth);

            var downloadFileName =
                $"Attendance_Report_{companySafeName}_{monthName}_{selectedYear}.xlsx";

            return File(
                fileBytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                downloadFileName);
        }


        // ============================================================
        // WORKING DAY RULE
        // ============================================================

        private static bool IsWorkingDay(DateTime date)
        {
            // Sunday = weekly off
            if (date.DayOfWeek == DayOfWeek.Sunday)
                return false;

            // Saturday
            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                var weekOfMonth =
                    ((date.Day - 1) / 7) + 1;

                // 2nd and 4th Saturday = weekly off
                if (weekOfMonth == 2 ||
                    weekOfMonth == 4)
                {
                    return false;
                }

                // 1st, 3rd and 5th Saturday = working
                return true;
            }

            // Monday-Friday
            return true;
        }

        // ============================================================
        // INDIA ATTENDANCE DATE
        // ============================================================

        private static DateTime GetIndiaAttendanceDate(
            DateTime indiaDate)
        {
            var date = new DateTime(
                indiaDate.Year,
                indiaDate.Month,
                indiaDate.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified);

            return DateTime.SpecifyKind(
                date
                    .AddHours(-5)
                    .AddMinutes(-30),
                DateTimeKind.Utc);
        }

        // ============================================================
        // UTC -> INDIA DATE
        // ============================================================

        private static DateTime ConvertAttendanceDateToIndiaDate(
            DateTime utcDate)
        {
            return DateTime.SpecifyKind(
                    utcDate,
                    DateTimeKind.Utc)
                .AddHours(5)
                .AddMinutes(30)
                .Date;
        }

        // ============================================================
        // UTC -> INDIA TIME
        // ============================================================

        private static DateTime? ConvertUtcToIndiaTime(
            DateTime? utcDateTime)
        {
            if (!utcDateTime.HasValue)
                return null;

            return DateTime.SpecifyKind(
                    utcDateTime.Value,
                    DateTimeKind.Utc)
                .AddHours(5)
                .AddMinutes(30);
        }

        // ============================================================
        // FORMAT WORKING HOURS
        // ============================================================

        private static string FormatWorkingHours(
            decimal workingHours)
        {
            if (workingHours <= 0)
                return "00:00";

            var totalMinutes =
                (int)Math.Round(
                    workingHours * 60m);

            var hours =
                totalMinutes / 60;

            var minutes =
                totalMinutes % 60;

            return $"{hours}:{minutes:00}";
        }


        // ============================================================
        // EMPLOYEE TOTAL TEXT
        // ============================================================

        private static string BuildEmployeeTotalText(
            decimal totalWorkingHours,
            decimal presentDays,
            int leaveDays,
            int holidayDays,
            int absentDays,
            int weeklyOffDays)
        {
            var totalMinutes =
                (int)Math.Round(
                    totalWorkingHours * 60m);

            var hours =
                totalMinutes / 60;

            var minutes =
                totalMinutes % 60;

            return
                $"Total Duration={hours} Hrs {minutes} Min , " +
                $"PresentDays={presentDays:0.##} , " +
                $"Leaves={leaveDays} , " +
                $"Holiday={holidayDays} , " +
                $"AbsentDays={absentDays} , " +
                $"Weekly Off ={weeklyOffDays}";
        }


        // ============================================================
        // SAFE FILE NAME
        // ============================================================

        private static string MakeSafeFileName(
            string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return "Company";

            var invalidCharacters =
                Path.GetInvalidFileNameChars();

            var result =
                new string(
                    fileName
                        .Select(c =>
                            invalidCharacters.Contains(c)
                                ? '_'
                                : c)
                        .ToArray());

            return result.Trim();
        }
        // ============================================================
        // REPORT ROW MODELS
        // ============================================================

        public class AttendanceReportRow
        {
            public int EmployeeId { get; set; }

            public string EmployeeCode { get; set; } = "";

            public string EmployeeName { get; set; } = "";

            public string? Department { get; set; }

            public string? Designation { get; set; }

            public DateTime? JoiningDate { get; set; }

            public int WorkingDays { get; set; }

            public int Present { get; set; }

            public int HalfDay { get; set; }

            public int Absent { get; set; }

            public int Leave { get; set; }

            public int Late { get; set; }

            public int NoPunchIn { get; set; }

            public int NoOutPunch { get; set; }

            public decimal TotalWorkingHours { get; set; }

            public decimal AttendancePercentage { get; set; }
        }

        public class AttendanceDailyRow
        {
            public DateTime AttendanceDate { get; set; }

            public string EmployeeCode { get; set; } = "";

            public string EmployeeName { get; set; } = "";

            public string? Department { get; set; }

            public string? Status { get; set; }

            public DateTime? CheckIn { get; set; }

            public DateTime? CheckOut { get; set; }

            public string? CheckInLocationName { get; set; }
            public string? CheckOutLocationName { get; set; }

            public decimal? WorkingHours { get; set; }

            public int? LateMinutes { get; set; }

            public int? EarlyLeaveMinutes { get; set; }

            public string? AttendanceMode { get; set; }

            public string? LateReviewStatus { get; set; }
        }

        public class AttendanceLateRow
        {
            public DateTime AttendanceDate { get; set; }

            public string EmployeeCode { get; set; } = "";

            public string EmployeeName { get; set; } = "";

            public string? Department { get; set; }

            public DateTime? CheckIn { get; set; }

            public int LateMinutes { get; set; }

            public int? LateMarkNumber { get; set; }

            public string ReviewStatus { get; set; } = "";

            public string? Reason { get; set; }
        }
    }
}