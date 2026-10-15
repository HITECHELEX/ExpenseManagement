using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using System.Security.Claims;

using ExpenseManagement.Services;
namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Employee,HR,Management")]
    public class AttendanceController : Controller
    {
        private readonly AppDbContext _db;

        public AttendanceController(AppDbContext db)
        {
            _db = db;
        }

        private int CurrentCompanyId =>
            int.Parse(User.FindFirstValue("CompanyId")!);

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private static DateTime IndiaNow =>
    DateTime.UtcNow.AddHours(5).AddMinutes(30);

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

            // Convert IST midnight to UTC midnight-equivalent instant.
            return DateTime.SpecifyKind(
                date.AddHours(-5).AddMinutes(-30),
                DateTimeKind.Utc);
        }

        // ============================================================
        // ATTENDANCE INDEX
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Index(DateTime? date)
        {
            // Calendar date displayed to the user in India.
            var selectedDate = date.HasValue
                ? date.Value.Date
                : IndiaNow.Date;

            // Database value representing midnight IST for this date.
            var attendanceDate = GetIndiaAttendanceDate(selectedDate);

            var employees = await _db.Employees
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive" &&
                    (!e.JoiningDate.HasValue ||
                     e.JoiningDate.Value.Date <= selectedDate)
                )
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            var attendanceRecords = await _db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.AttendanceDate == attendanceDate &&
                    a.Employee.User.CompanyId == CurrentCompanyId
                )
                .ToListAsync();

            var pendingLateReviews = await _db.Attendances
    .Include(a => a.Employee)
        .ThenInclude(e => e.User)
    .Where(a =>
        a.Employee.User.CompanyId == CurrentCompanyId &&
        a.IsLate &&
        a.LateReviewStatus == "Pending")
    .OrderBy(a => a.AttendanceDate)
    .ThenBy(a => a.LateMarkNumber)
    .ToListAsync();

            ViewBag.PendingLateReviews = pendingLateReviews;

            var attendanceByEmployee = attendanceRecords
    .ToDictionary(a => a.EmployeeId);

            // ============================================================
            // ENSURE APPROVED LEAVE IS REFLECTED IN DAILY ATTENDANCE
            // ============================================================

            var approvedLeavesForSelectedDate = await _db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.Employee.User.CompanyId == CurrentCompanyId &&
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.TotalDays >= 1 &&
                    l.FromDate.Date <= selectedDate &&
                    l.ToDate.Date >= selectedDate)
                .ToListAsync();

            foreach (var leave in approvedLeavesForSelectedDate)
            {
                // Do not overwrite an existing attendance record.
                if (attendanceByEmployee.ContainsKey(leave.EmployeeId))
                    continue;

                var attendance = new Attendance
                {
                    EmployeeId = leave.EmployeeId,
                    AttendanceDate = attendanceDate,
                    Status = "On Leave",
                    Source = "System",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _db.Attendances.Add(attendance);

                // Immediately make the new record available to the HR view.
                attendanceByEmployee[leave.EmployeeId] = attendance;
            }

            if (_db.ChangeTracker.HasChanges())
            {
                await _db.SaveChangesAsync();
            }

            // ============================================================
            // HR MONTHLY ATTENDANCE SUMMARY
            // ============================================================

            var monthStart = new DateTime(
                selectedDate.Year,
                selectedDate.Month,
                1);

            var nextMonthStart = monthStart.AddMonths(1);

            // Company holidays for the selected month.
            var holidays = await WorkCalendar.LoadHolidaysAsync(
                _db, CurrentCompanyId, monthStart, nextMonthStart.AddDays(-1));

            // Convert India month boundaries to the UTC representation
            // used by Attendance.AttendanceDate.
            var monthStartUtc = GetIndiaAttendanceDate(monthStart);
            var nextMonthStartUtc = GetIndiaAttendanceDate(nextMonthStart);

            // Get all attendance records for the selected month
            // belonging only to the current HR company.
            var monthlyAttendance = await _db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.AttendanceDate >= monthStartUtc &&
                    a.AttendanceDate < nextMonthStartUtc &&
                    a.Employee.User.CompanyId == CurrentCompanyId)
                .ToListAsync();

            var monthlyApprovedLeaves = await _db.Leaves
    .AsNoTracking()
    .Where(l =>
        l.Employee.User.CompanyId == CurrentCompanyId &&
        l.Status == "Approved" &&
        l.CancelledOn == null &&
        l.FromDate.Date < nextMonthStart &&
        l.ToDate.Date >= monthStart)
    .ToListAsync();

            // ============================================================
            // BUILD HR MONTHLY ATTENDANCE SUMMARY
            // ============================================================

            var monthlySummary = employees
                .Select(employee =>
                {
                    var employeeAttendance = monthlyAttendance
                        .Where(a => a.EmployeeId == employee.Id)
                        .ToList();

                    var employeeApprovedLeaves = monthlyApprovedLeaves
                        .Where(l => l.EmployeeId == employee.Id)
                        .ToList();

                    var workingDays = 0;

                    for (var day = monthStart;
                         day < nextMonthStart;
                         day = day.AddDays(1))
                    {
                        // Do not count days before employee joined.
                        if (employee.JoiningDate.HasValue &&
                            day.Date < employee.JoiningDate.Value.Date)
                        {
                            continue;
                        }

                        // Days after the exit date are not counted.
                        if (employee.ExitDate.HasValue &&
                            day.Date > employee.ExitDate.Value.Date)
                        {
                            continue;
                        }

                        // Weekly off OR company holiday.
                        var isWeeklyOff = WorkCalendar.IsNonWorkingDay(day, holidays);

                        if (!isWeeklyOff)
                        {
                            workingDays++;
                        }
                    }

                    var presentCount = 0;
                    var halfDayCount = 0;
                    var absentCount = 0;
                    var leaveCount = 0;
                    var noPunchInCount = 0;
                    var noOutPunchCount = 0;

                    var lateCount = employeeAttendance.Count(a =>
                        a.IsLate &&
                        a.LateReviewStatus != "Approved");

                    // Process the attendance records that actually exist.
                    foreach (var attendance in employeeAttendance)
                    {
                        if (attendance.Status == "On Leave")
                        {
                            leaveCount++;
                            continue;
                        }

                        // Both punches exist.
                        if (attendance.CheckIn.HasValue &&
                            attendance.CheckOut.HasValue)
                        {
                            if (attendance.Status == "Half Day")
                                halfDayCount++;
                            else
                                presentCount++;

                            continue;
                        }

                        // Check-out exists but check-in is missing.
                        // Still considered Present, but flagged as No Punch-In.
                        if (!attendance.CheckIn.HasValue &&
                            attendance.CheckOut.HasValue)
                        {
                            presentCount++;
                            noPunchInCount++;
                            continue;
                        }

                        // Check-in exists but check-out is missing.
                        // Still considered Present, but flagged as No Out-Punch.
                        if (attendance.CheckIn.HasValue &&
                            !attendance.CheckOut.HasValue)
                        {
                            presentCount++;
                            noOutPunchCount++;
                            continue;
                        }

                        // Attendance record exists but neither punch exists.
                        // If it is not approved leave, it is Absent.
                        if (!attendance.CheckIn.HasValue &&
                            !attendance.CheckOut.HasValue)
                        {
                            absentCount++;
                        }
                    }

                    // Count elapsed working days where there is no attendance
                    // record and no approved leave.
                    var todayIndia = IndiaNow.Date;

                    DateTime? lastDateToEvaluate = null;

                    if (selectedDate.Year < todayIndia.Year ||
                        (selectedDate.Year == todayIndia.Year &&
                         selectedDate.Month < todayIndia.Month))
                    {
                        // Past month — evaluate the entire month.
                        lastDateToEvaluate = nextMonthStart.AddDays(-1);
                    }
                    else if (selectedDate.Year == todayIndia.Year &&
                             selectedDate.Month == todayIndia.Month)
                    {
                        // Current month — evaluate only up to today.
                        lastDateToEvaluate = todayIndia;
                    }

                    // Future month → lastDateToEvaluate remains null.

                    if (lastDateToEvaluate.HasValue)
                    {
                        for (var day = monthStart;
                             day <= lastDateToEvaluate.Value;
                             day = day.AddDays(1))
                        {
                            if (employee.JoiningDate.HasValue &&
                                day.Date < employee.JoiningDate.Value.Date)
                            {
                                continue;
                            }

                            // Days after the exit date are not counted.
                            if (employee.ExitDate.HasValue &&
                                day.Date > employee.ExitDate.Value.Date)
                            {
                                continue;
                            }

                            // Weekly off OR company holiday.
                            var isWeeklyOff = WorkCalendar.IsNonWorkingDay(day, holidays);

                            if (isWeeklyOff)
                                continue;

                            var dayAttendance = employeeAttendance
                                .FirstOrDefault(a =>
                                    a.AttendanceDate == GetIndiaAttendanceDate(day));

                            if (dayAttendance != null)
                            {
                                // Attendance already exists.
                                // Its status has already been handled above.
                                continue;
                            }

                            var hasApprovedLeave = employeeApprovedLeaves.Any(l =>
                                l.FromDate.Date <= day.Date &&
                                l.ToDate.Date >= day.Date);

                            if (hasApprovedLeave)
                            {
                                // No attendance row, but approved leave exists.
                                leaveCount++;
                            }
                            else
                            {
                                // Working day, employee had joined,
                                // no attendance record and no approved leave.
                                absentCount++;
                            }
                        }
                    }


                    return new
                    {
                        EmployeeId = employee.Id,
                        EmployeeCode = employee.EmployeeCode,
                        EmployeeName = employee.User.FullName,
                        WorkingDays = workingDays,
                        PresentCount = presentCount,
                        LateCount = lateCount,
                        HalfDayCount = halfDayCount,
                        AbsentCount = absentCount,
                        LeaveCount = leaveCount,
                        NoPunchInCount = noPunchInCount,
                        NoOutPunchCount = noOutPunchCount
                    };
                })
                .OrderBy(x => x.EmployeeName)
                .ToList();

            ViewBag.MonthlySummary = monthlySummary;

            ViewBag.AttendanceDate = selectedDate;
            ViewBag.AttendanceRecords = attendanceByEmployee;

            ViewBag.MonthlyAttendance = monthlyAttendance;
            ViewBag.MonthStart = monthStart;

            return View(
                "~/Views/HR/AttendenceView.cshtml",
                employees);
        }


        // ============================================================
        // CHECK-IN
        // ============================================================
        // ============================================================
        // EMPLOYEE - CHECK-IN
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(
    string mode,
    string? visitLocation,
    decimal? latitude,
    decimal? longitude,
    decimal? accuracy,
    string? locationName)
        {
            if (mode != "InOffice" && mode != "Visit")
            {
                TempData["AttendanceMessage"] =
                    "Please select an attendance mode before checking in.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = IndiaNow.Date });
            }

            if (!IsValidGps(latitude, longitude, accuracy))
            {
                TempData["AttendanceMessage"] =
                    "Unable to capture a valid device location. Please allow location access and try again.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = IndiaNow.Date });
            }

            var today = IndiaNow.Date;
            var attendanceDate = GetIndiaAttendanceDate(today);

            // Find the logged-in employee only.
            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive");

            if (employee == null)
                return NotFound();

            var todayIndia = DateTime.UtcNow
    .AddHours(5)
    .AddMinutes(30)
    .Date;

            var leaveFromDate = new DateTime(
                todayIndia.Year,
                todayIndia.Month,
                todayIndia.Day,
                0, 0, 0,
                DateTimeKind.Unspecified);

            var leaveToDate = leaveFromDate.AddDays(1).AddTicks(-1);

            var approvedLeave = await _db.Leaves
    .AsNoTracking()
    .FirstOrDefaultAsync(l =>
        l.EmployeeId == employee.Id &&
        l.Status == "Approved" &&
        l.CancelledOn == null &&
        l.FromDate <= leaveToDate &&
        l.ToDate >= leaveFromDate &&
        l.TotalDays >= 1);

            if (approvedLeave != null)
            {
                TempData["AttendanceMessage"] =
                    "You cannot check in because you have an approved full-day leave for today.";

                return RedirectToAction(
    "MyAttendance",
    "HRMEmployee",
    new { date = todayIndia });
            }

            var attendance = await _db.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.Id &&
                    a.AttendanceDate == attendanceDate);

            if (attendance == null)
            {
                attendance = new Attendance
                {
                    EmployeeId = employee.Id,
                    AttendanceDate = attendanceDate,
                    AttendanceMode = mode,
                    VisitLocation = mode == "Visit"
                        ? visitLocation?.Trim()
                        : null,
                    Source = "EmployeeWeb",
                    CreatedAt = DateTime.UtcNow
                };

                _db.Attendances.Add(attendance);
            }

            // Prevent duplicate Check-In.
            if (attendance.CheckIn.HasValue)
            {
                TempData["AttendanceMessage"] =
                    "You have already checked in today.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = today });
            }

            // Server-generated current time.
            var checkInTime = DateTime.UtcNow;
            attendance.CheckIn = checkInTime;

            // Preserve the original employee punch.
            attendance.OriginalCheckIn ??= checkInTime;

            attendance.CheckInLatitude = latitude;
            attendance.CheckInLongitude = longitude;
            attendance.CheckInAccuracy = accuracy;
            attendance.CheckInLocationName =
                string.IsNullOrWhiteSpace(locationName)
                    ? null
                    : locationName.Trim();

            attendance.Source = "EmployeeWeb";
            attendance.UpdatedAt = DateTime.UtcNow;

            // --------------------------------------------------------
            // Calculate Late Minutes
            // --------------------------------------------------------

            var currentIndiaTime = IndiaNow;
            var officialStart = today.AddHours(9).AddMinutes(45);
            var mandatoryReviewTime = today.AddHours(10).AddMinutes(30);


            // ------------------------------------------------------------
            // Calculate Late Minutes
            // ------------------------------------------------------------

            if (currentIndiaTime > officialStart)
            {
                attendance.LateMinutes =
                    (int)(currentIndiaTime - officialStart).TotalMinutes;
            }
            else
            {
                attendance.LateMinutes = 0;
            }


            // ------------------------------------------------------------
            // In Office Late Attendance
            // ------------------------------------------------------------

            if (mode == "InOffice" &&
    attendance.LateMinutes > 0)
            {
                attendance.IsLate = true;

                // --------------------------------------------------------
                // Count previous valid late marks.
                //
                // Approved late reviews do not count.
                // --------------------------------------------------------

                // Late marks reset every calendar month (India date).
                var lateMonthStartUtc = GetIndiaAttendanceDate(
                    new DateTime(
                        attendanceDate.AddHours(5).AddMinutes(30).Year,
                        attendanceDate.AddHours(5).AddMinutes(30).Month,
                        1));

                var previousValidLateCount =
                    await _db.Attendances
                        .CountAsync(a =>
                            a.EmployeeId == employee.Id &&
                            a.AttendanceMode == "InOffice" &&
                            a.IsLate &&
                            a.LateReviewStatus != "Approved" &&
                            a.AttendanceDate < attendanceDate &&
                            a.AttendanceDate >= lateMonthStartUtc);

                attendance.LateMarkNumber =
                    previousValidLateCount + 1;


                // --------------------------------------------------------
                // 10:30 AM rule
                // --------------------------------------------------------

                if (currentIndiaTime >= mandatoryReviewTime)
                {
                    // HR review is mandatory.
                    attendance.LateReviewStatus = "Required";
                }
                else
                {
                    // HR review is optional.
                    attendance.LateReviewStatus = null;
                }


                // --------------------------------------------------------
                // 4th valid late
                //
                // The Half Day consequence is applied only when the
                // 4th late is finally valid.
                //
                // While review is pending, we do NOT immediately make
                // the employee Half Day.
                // --------------------------------------------------------

                if (attendance.LateMarkNumber == 4)
                {
                    if (attendance.LateReviewStatus == "Required")
                    {
                        // Mandatory review is pending.
                        // Do not finalize Half Day yet.
                        attendance.Status = "Late — Review Pending";
                    }
                    else
                    {
                        // Optional review has not been requested.
                        // The 4th late is currently valid.
                        attendance.Status = "Half Day";
                    }
                }
                else
                {
                    attendance.Status = "No Out-Punch";
                }
            }
            else
            {
                // --------------------------------------------------------
                // On-time attendance OR Visit attendance
                // --------------------------------------------------------

                attendance.IsLate = false;
                attendance.LateMarkNumber = null;
                attendance.LateReviewStatus = null;

                attendance.Status = "No Out-Punch";
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Unique (employee, date) index: a double-click or a second
                // device already recorded today's attendance.
                TempData["AttendanceMessage"] =
                    "Your attendance for today has already been recorded.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = today });
            }

            TempData["AttendanceMessage"] =
                "Check-In recorded successfully.";

            return RedirectToAction(
                "MyAttendance",
                "HRMEmployee",
                new { date = today });
        }

        // ============================================================
        // CHECK-OUT
        // ============================================================
        // ============================================================
        // EMPLOYEE - CHECK-OUT
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(
    decimal? latitude,
    decimal? longitude,
    decimal? accuracy,
    string? locationName)
        {
            var today = IndiaNow.Date;
            var attendanceDate = GetIndiaAttendanceDate(today);

            if (!IsValidGps(latitude, longitude, accuracy))
            {
                TempData["AttendanceMessage"] =
                    "Unable to capture a valid device location. Please allow location access and try again.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = today });
            }

            // Find the logged-in employee only.
            var employee = await _db.Employees
                .Include(e => e.User)
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive");

            if (employee == null)
                return NotFound();

            // ------------------------------------------------------------
            // Block Check-Out when the employee has approved full-day leave.
            // ------------------------------------------------------------

            var todayIndia = IndiaNow.Date;

            var leaveFromDate = new DateTime(
                todayIndia.Year,
                todayIndia.Month,
                todayIndia.Day,
                0,
                0,
                0,
                DateTimeKind.Unspecified);

            var leaveToDate =
                leaveFromDate.AddDays(1).AddTicks(-1);

            var approvedLeave = await _db.Leaves
                .AsNoTracking()
                .FirstOrDefaultAsync(l =>
                    l.EmployeeId == employee.Id &&
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate <= leaveToDate &&
                    l.ToDate >= leaveFromDate &&
                    l.TotalDays >= 1);

            if (approvedLeave != null)
            {
                TempData["AttendanceMessage"] =
                    "You cannot check out because you have an approved full-day leave for today.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = todayIndia });
            }

            var attendance = await _db.Attendances
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == employee.Id &&
                    a.AttendanceDate == attendanceDate);

            if (attendance == null)
            {
                TempData["AttendanceMessage"] =
                    "You must check in before checking out.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = today });
            }

            // Check-In is required before Check-Out.
            if (!attendance.CheckIn.HasValue)
            {
                TempData["AttendanceMessage"] =
                    "You must check in before checking out.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = today });
            }

            // Prevent duplicate Check-Out.
            if (attendance.CheckOut.HasValue)
            {
                TempData["AttendanceMessage"] =
                    "You have already checked out today.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new { date = today });
            }

            // Server-generated current time.
            var checkOutTime = DateTime.UtcNow;

            attendance.CheckOut = checkOutTime;

            // Preserve the original employee punch.
            attendance.OriginalCheckOut ??= checkOutTime;

            attendance.CheckOutLatitude = latitude;
            attendance.CheckOutLongitude = longitude;
            attendance.CheckOutAccuracy = accuracy;
            attendance.CheckOutLocationName =
                string.IsNullOrWhiteSpace(locationName)
                    ? null
                    : locationName.Trim();

            attendance.Source = "EmployeeWeb";
            attendance.UpdatedAt = DateTime.UtcNow;

            // --------------------------------------------------------
            // Calculate Working Hours
            // --------------------------------------------------------

            var workingHours =
                (checkOutTime - attendance.CheckIn.Value).TotalHours;

            attendance.WorkingHours =
                Math.Round((decimal)workingHours, 2);

            // --------------------------------------------------------
            // Calculate Early Leave
            // --------------------------------------------------------

            var currentIndiaTime = IndiaNow;

            var officialEnd =
                today.AddHours(18);

            if (currentIndiaTime < officialEnd)
            {
                attendance.EarlyLeaveMinutes =
                    (int)(officialEnd - currentIndiaTime).TotalMinutes;
            }
            else
            {
                attendance.EarlyLeaveMinutes = 0;
            }

            // --------------------------------------------------------
            // Calculate Attendance Status
            //
            // > 4 hours  = Present
            // = 4 hours  = Half Day
            // < 4 hours  = Absent
            // --------------------------------------------------------

            // ------------------------------------------------------------
            // Final Attendance Status
            // ------------------------------------------------------------
            //
            // Priority:
            // 1. Mandatory late review pending
            // 2. 4th valid late → Half Day
            // 3. Existing working-hours rules
            // ------------------------------------------------------------

            if (attendance.LateReviewStatus == "Required")
            {
                // Employee was late at/after 10:30 AM and
                // mandatory HR review has not been completed.
                attendance.Status = "Late — Review Pending";
            }
            else if (attendance.IsLate &&
                     attendance.LateMarkNumber == 4 &&
                     attendance.LateReviewStatus != "Approved")
            {
                // 4th valid late.
                //
                // If review is optional and has not been requested,
                // the 4th late remains a Half Day.
                //
                // If HR later rejects the review, this condition
                // will also keep the Half Day consequence.
                attendance.Status = "Half Day";
            }
            else
            {
                // --------------------------------------------------------
                // Existing working-hours rules
                // --------------------------------------------------------

                if (workingHours > 4)
                {
                    attendance.Status = "Present";
                }
                else if (Math.Abs(workingHours - 4) < 0.01)
                {
                    attendance.Status = "Half Day";
                }
                else
                {
                    attendance.Status = "Absent";
                }
            }

            await _db.SaveChangesAsync();

            TempData["AttendanceMessage"] =
                "Check-Out recorded successfully.";

            return RedirectToAction(
                "MyAttendance",
                "HRMEmployee",
                new { date = today });
        }

        // ============================================================
        // Employee Late Attendance Review
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestLateReview(
            int id,
            string lateReason)
        {
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee &&
                    e.EmploymentStatus != "Inactive");

            if (employee == null)
                return Forbid();


            // --------------------------------------------------------
            // Find the employee's attendance record.
            // --------------------------------------------------------

            var attendance = await _db.Attendances
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    a.EmployeeId == employee.Id);

            if (attendance == null)
                return NotFound();


            // --------------------------------------------------------
            // Only late attendance can be reviewed.
            // --------------------------------------------------------

            if (!attendance.IsLate ||
                !attendance.LateMarkNumber.HasValue)
            {
                TempData["AttendanceMessage"] =
                    "This attendance record does not have a valid late mark.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new
                    {
                        date = DateTime.UtcNow
                            .AddHours(5)
                            .AddMinutes(30)
                            .Date
                    });
            }


            // --------------------------------------------------------
            // Reason is mandatory when requesting a review.
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(lateReason))
            {
                TempData["AttendanceMessage"] =
                    "Please provide a reason for the late attendance.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new
                    {
                        date = attendance.AttendanceDate
                            .AddHours(5)
                            .AddMinutes(30)
                            .Date
                    });
            }


            // --------------------------------------------------------
            // Do not allow another review after HR has already
            // completed the review.
            // --------------------------------------------------------

            if (attendance.LateReviewStatus == "Approved" ||
                attendance.LateReviewStatus == "Rejected")
            {
                TempData["AttendanceMessage"] =
                    "This late attendance has already been reviewed by HR.";

                return RedirectToAction(
                    "MyAttendance",
                    "HRMEmployee",
                    new
                    {
                        date = attendance.AttendanceDate
                            .AddHours(5)
                            .AddMinutes(30)
                            .Date
                    });
            }


            // --------------------------------------------------------
            // Save the employee's review request.
            // --------------------------------------------------------

            attendance.LateReason = lateReason.Trim();

            attendance.LateReviewStatus = "Pending";

            attendance.LateReviewRemarks = null;

            attendance.LateReviewedBy = null;

            attendance.LateReviewedOn = null;

            attendance.UpdatedAt = DateTime.UtcNow;


            // --------------------------------------------------------
            // Important:
            //
            // Do NOT change IsLate here.
            // Do NOT change LateMarkNumber here.
            //
            // The late remains valid until HR makes a decision.
            // --------------------------------------------------------

            await _db.SaveChangesAsync();


            TempData["AttendanceMessage"] =
                "Late attendance review request submitted to HR.";


            return RedirectToAction(
                "MyAttendance",
                "HRMEmployee",
                new
                {
                    date = attendance.AttendanceDate
                        .AddHours(5)
                        .AddMinutes(30)
                        .Date
                });
        }

        // ============================================================
        // HR - REVIEW LATE ATTENDANCE
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewLate(
            int id,
            string decision,
            string? remarks)
        {
            // --------------------------------------------------------
            // Find the attendance record belonging to the HR user's
            // company.
            // --------------------------------------------------------

            var attendance = await _db.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e.User)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    a.Employee.User.CompanyId == CurrentCompanyId);

            if (attendance == null)
                return NotFound();


            // --------------------------------------------------------
            // Validate decision
            // --------------------------------------------------------

            if (decision != "Approved" &&
                decision != "Rejected")
            {
                TempData["Error"] =
                    "Invalid late review decision.";

                return RedirectToAction(
                    nameof(Index),
                    new
                    {
                        date = IndiaNow.Date
                    });
            }


            // --------------------------------------------------------
            // Only Pending reviews can be processed.
            // --------------------------------------------------------

            if (attendance.LateReviewStatus != "Pending")
            {
                TempData["Error"] =
                    "This late attendance has already been reviewed.";

                return RedirectToAction(
                    nameof(Index),
                    new
                    {
                        date = IndiaNow.Date
                    });
            }


            // --------------------------------------------------------
            // Save HR decision
            // --------------------------------------------------------

            attendance.LateReviewStatus = decision;

            attendance.LateReviewRemarks =
                string.IsNullOrWhiteSpace(remarks)
                    ? null
                    : remarks.Trim();

            attendance.LateReviewedBy = CurrentUserId;

            attendance.LateReviewedOn = DateTime.UtcNow;


            // ========================================================
            // APPROVED
            // ========================================================

            if (decision == "Approved")
            {
                // The late is no longer valid.
                attendance.IsLate = false;

                // Remove this occurrence from the valid late count.
                attendance.LateMarkNumber = null;


                // ----------------------------------------------------
                // Recalculate the attendance status based on the
                // actual working hours.
                // ----------------------------------------------------

                if (attendance.WorkingHours.HasValue)
                {
                    var workingHours =
                        (double)attendance.WorkingHours.Value;

                    if (workingHours > 4)
                    {
                        attendance.Status = "Present";
                    }
                    else if (Math.Abs(workingHours - 4) < 0.01)
                    {
                        attendance.Status = "Half Day";
                    }
                    else
                    {
                        attendance.Status = "Absent";
                    }
                }
                else if (attendance.CheckIn.HasValue &&
                         !attendance.CheckOut.HasValue)
                {
                    // Employee has not checked out yet.
                    attendance.Status = "No Out-Punch";
                }
            }


            // ========================================================
            // REJECTED
            // ========================================================

            else if (decision == "Rejected")
            {
                // The late remains valid.
                attendance.IsLate = true;


                // ----------------------------------------------------
                // 4th valid late remains Half Day.
                // ----------------------------------------------------

                if (attendance.LateMarkNumber == 4)
                {
                    attendance.Status = "Half Day";
                }
                else if (attendance.WorkingHours.HasValue)
                {
                    var workingHours =
                        (double)attendance.WorkingHours.Value;

                    if (workingHours > 4)
                    {
                        attendance.Status = "Present";
                    }
                    else if (Math.Abs(workingHours - 4) < 0.01)
                    {
                        attendance.Status = "Half Day";
                    }
                    else
                    {
                        attendance.Status = "Absent";
                    }
                }
                else if (attendance.CheckIn.HasValue &&
                         !attendance.CheckOut.HasValue)
                {
                    attendance.Status = "No Out-Punch";
                }
            }


            attendance.UpdatedAt = DateTime.UtcNow;

            WriteAudit("LateReview", attendance.Id,
                $"Late attendance review {decision} for employee {attendance.EmployeeId} on {attendance.AttendanceDate.AddHours(5).AddMinutes(30):dd-MMM-yyyy}.");

            await _db.SaveChangesAsync();


            // --------------------------------------------------------
            // Success message
            // --------------------------------------------------------

            TempData["Success"] =
                decision == "Approved"
                    ? "Late attendance review approved successfully."
                    : "Late attendance review rejected successfully.";


            // --------------------------------------------------------
            // Return to the EXISTING HR Attendance page.
            // No separate LateReviews view.
            // --------------------------------------------------------

            return RedirectToAction(
                nameof(Index),
                new
                {
                    date = IndiaNow.Date
                });
        }



        // ============================================================
        // HR - EDIT ATTENDANCE
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var attendance = await _db.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e.User)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    a.Employee.User.CompanyId == CurrentCompanyId &&
                    a.Employee.User.Role == UserRole.Employee);

            if (attendance == null)
                return NotFound();

            return View("~/Views/HR/EditAttendance.cshtml", attendance);
        }

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
    int id,
    DateTime? checkIn,
    DateTime? checkOut,
    string? status,
    string? remarks)
        {
            var attendance = await _db.Attendances
                .Include(a => a.Employee)
                    .ThenInclude(e => e.User)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    a.Employee.User.CompanyId == CurrentCompanyId &&
                    a.Employee.User.Role == UserRole.Employee);

            if (attendance == null)
                return NotFound();

            // ------------------------------------------------------------
            // Preserve original punch times
            // ------------------------------------------------------------

            if (attendance.OriginalCheckIn == null &&
                attendance.CheckIn.HasValue)
            {
                attendance.OriginalCheckIn = attendance.CheckIn;
            }

            if (attendance.OriginalCheckOut == null &&
                attendance.CheckOut.HasValue)
            {
                attendance.OriginalCheckOut = attendance.CheckOut;
            }

            // ------------------------------------------------------------
            // Convert HR-entered IST time to UTC
            // ------------------------------------------------------------

            if (checkIn.HasValue)
            {
                attendance.CheckIn =
                    ConvertIndiaTimeToUtc(checkIn.Value);
            }
            else
            {
                attendance.CheckIn = null;
            }

            if (checkOut.HasValue)
            {
                attendance.CheckOut =
                    ConvertIndiaTimeToUtc(checkOut.Value);
            }
            else
            {
                attendance.CheckOut = null;
            }

            // ------------------------------------------------------------
            // Validate Check-Out
            // ------------------------------------------------------------

            if (attendance.CheckIn.HasValue &&
                attendance.CheckOut.HasValue &&
                attendance.CheckOut.Value <= attendance.CheckIn.Value)
            {
                ModelState.AddModelError(
                    "",
                    "Check-Out time must be later than Check-In time.");

                return View("~/Views/HR/EditAttendance.cshtml", attendance);
            }

            // ------------------------------------------------------------
            // Calculate Late Minutes + Late Attendance
            // ------------------------------------------------------------

            attendance.LateMinutes = null;

            if (attendance.CheckIn.HasValue)
            {
                var indiaCheckIn =
                    TimeZoneInfo.ConvertTimeFromUtc(
                        attendance.CheckIn.Value.ToUniversalTime(),
                        TimeZoneInfo.Utc)
                    .AddHours(5)
                    .AddMinutes(30);

                var officialStart =
                    indiaCheckIn.Date.AddHours(9).AddMinutes(45);

                var mandatoryReviewTime =
                    indiaCheckIn.Date.AddHours(10).AddMinutes(30);

                if (indiaCheckIn > officialStart)
                {
                    attendance.LateMinutes =
                        (int)(indiaCheckIn - officialStart).TotalMinutes;
                }
                else
                {
                    attendance.LateMinutes = 0;
                }

                // --------------------------------------------------------
                // Recalculate In-Office Late Attendance
                // --------------------------------------------------------

                if (attendance.AttendanceMode == "InOffice" &&
                    attendance.LateMinutes > 0)
                {
                    attendance.IsLate = true;

                    // Recalculate the late mark number based on
                    // currently valid late records.
                    // Late marks reset every calendar month (India date).
                    var lateMonthStartUtc = GetIndiaAttendanceDate(
                        new DateTime(
                            attendance.AttendanceDate.AddHours(5).AddMinutes(30).Year,
                            attendance.AttendanceDate.AddHours(5).AddMinutes(30).Month,
                            1));

                    var previousValidLateCount =
                        await _db.Attendances
                            .CountAsync(a =>
                                a.Id != attendance.Id &&
                                a.EmployeeId == attendance.EmployeeId &&
                                a.AttendanceMode == "InOffice" &&
                                a.IsLate &&
                                a.LateReviewStatus != "Approved" &&
                                a.AttendanceDate < attendance.AttendanceDate &&
                                a.AttendanceDate >= lateMonthStartUtc);

                    attendance.LateMarkNumber =
                        previousValidLateCount + 1;

                    // HR editing the attendance determines whether
                    // review is required based on the corrected Check-In.
                    if (indiaCheckIn >= mandatoryReviewTime)
                    {
                        attendance.LateReviewStatus = "Required";
                    }
                    else
                    {
                        attendance.LateReviewStatus = null;
                    }

                    // A 4th valid late becomes Half Day.
                    if (attendance.LateMarkNumber == 4)
                    {
                        attendance.Status = "Half Day";
                    }
                }
                else
                {
                    // ----------------------------------------------------
                    // Corrected Check-In is no longer late
                    // ----------------------------------------------------

                    attendance.IsLate = false;
                    attendance.LateMarkNumber = null;

                    // The late mark is removed by the HR correction.
                    // Preserve an already completed review as history,
                    // but clear an unresolved review.
                    if (attendance.LateReviewStatus != "Approved" &&
                        attendance.LateReviewStatus != "Rejected")
                    {
                        attendance.LateReviewStatus = null;
                        attendance.LateReason = null;
                        attendance.LateReviewRemarks = null;
                        attendance.LateReviewedBy = null;
                        attendance.LateReviewedOn = null;
                    }
                }
            }
            else
            {
                attendance.LateMinutes = null;
                attendance.IsLate = false;
                attendance.LateMarkNumber = null;

                if (attendance.LateReviewStatus != "Approved" &&
                    attendance.LateReviewStatus != "Rejected")
                {
                    attendance.LateReviewStatus = null;
                    attendance.LateReason = null;
                    attendance.LateReviewRemarks = null;
                    attendance.LateReviewedBy = null;
                    attendance.LateReviewedOn = null;
                }
            }

            // ------------------------------------------------------------
            // Calculate Working Hours and Early Leave
            // ------------------------------------------------------------

            attendance.WorkingHours = null;
            attendance.EarlyLeaveMinutes = null;

            if (attendance.CheckIn.HasValue &&
                attendance.CheckOut.HasValue)
            {
                var workingHours =
                    (attendance.CheckOut.Value -
                     attendance.CheckIn.Value).TotalHours;

                attendance.WorkingHours =
                    Math.Round((decimal)workingHours, 2);

                var indiaCheckOut =
                    TimeZoneInfo.ConvertTimeFromUtc(
                        attendance.CheckOut.Value.ToUniversalTime(),
                        TimeZoneInfo.Utc)
                    .AddHours(5)
                    .AddMinutes(30);

                var officialEnd =
                    indiaCheckOut.Date.AddHours(18);

                if (indiaCheckOut < officialEnd)
                {
                    attendance.EarlyLeaveMinutes =
                        (int)(officialEnd - indiaCheckOut).TotalMinutes;
                }
                else
                {
                    attendance.EarlyLeaveMinutes = 0;
                }

                // --------------------------------------------------------
                // Attendance status
                // --------------------------------------------------------

                if (attendance.LateReviewStatus == "Required")
                {
                    attendance.Status = "Late — Review Pending";
                }
                else if (attendance.IsLate &&
                         attendance.LateMarkNumber == 4 &&
                         attendance.LateReviewStatus != "Approved")
                {
                    attendance.Status = "Half Day";
                }
                else if (workingHours > 4)
                {
                    attendance.Status = "Present";
                }
                else if (Math.Abs(workingHours - 4) < 0.01)
                {
                    attendance.Status = "Half Day";
                }
                else
                {
                    attendance.Status = "Absent";
                }
            }
            else if (attendance.CheckIn.HasValue)
            {
                attendance.Status = "No Out-Punch";
            }
            else
            {
                attendance.Status = status ?? "No Punch-In";
            }

            // ------------------------------------------------------------
            // HR correction information
            // ------------------------------------------------------------

            attendance.IsCorrectionRequested = true;
            attendance.CorrectionRequestedOn = DateTime.UtcNow;
            attendance.CorrectionRemarks = remarks;
            attendance.CorrectionStatus = "Approved";
            attendance.ApprovedBy = CurrentUserId;
            attendance.ApprovedOn = DateTime.UtcNow;
            attendance.UpdatedAt = DateTime.UtcNow;

            WriteAudit("Correction", attendance.Id,
                $"HR corrected attendance for employee {attendance.EmployeeId} on {attendance.AttendanceDate.AddHours(5).AddMinutes(30):dd-MMM-yyyy}. Status: {attendance.Status}.");

            await _db.SaveChangesAsync();

            TempData["AttendanceMessage"] =
    "Attendance record updated successfully.";

            var redirectDate = DateTime.SpecifyKind(
    attendance.AttendanceDate,
    DateTimeKind.Utc)
    .AddHours(5)
    .AddMinutes(30)
    .Date;

            return RedirectToAction(
                nameof(Index),
                new
                {
                    date = redirectDate
                });
        }

        private static bool IsValidGps(
    decimal? latitude,
    decimal? longitude,
    decimal? accuracy)
        {
            return latitude.HasValue &&
                   longitude.HasValue &&
                   accuracy.HasValue &&
                   latitude.Value >= -90m &&
                   latitude.Value <= 90m &&
                   longitude.Value >= -180m &&
                   longitude.Value <= 180m &&
                   accuracy.Value >= 0m;
        }
        private static DateTime ConvertIndiaTimeToUtc(DateTime indiaTime)
        {
            var unspecifiedIndiaTime = DateTime.SpecifyKind(
                indiaTime,
                DateTimeKind.Unspecified);

            var utcTime = unspecifiedIndiaTime
                .AddHours(-5)
                .AddMinutes(-30);

            return DateTime.SpecifyKind(
                utcTime,
                DateTimeKind.Utc);
        }

        private void WriteAudit(string action, int? entityId, string description)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = CurrentCompanyId,
                UserId = CurrentUserId,
                Action = action,
                EntityName = "Attendance",
                EntityId = entityId,
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }

        // ============================================================
        // INITIAL ATTENDANCE STATUS
        // ============================================================

        private static string GetInitialAttendanceStatus(DateTime date)
        {
            // Sunday
            if (date.DayOfWeek == DayOfWeek.Sunday)
                return "Weekly Off";

            // Saturday
            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                int occurrence = ((date.Day - 1) / 7) + 1;

                // 2nd and 4th Saturday = Holiday
                if (occurrence == 2 || occurrence == 4)
                    return "Holiday";

                // 1st, 3rd and 5th Saturday = Working
                return "No Punch-In";
            }

            // Normal working day
            return "No Punch-In";
        }
    }
}