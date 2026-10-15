using ExpenseManagement.Data;
using ExpenseManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseManagement.Services
{
    public class AttendanceProcessingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AttendanceProcessingService> _logger;

        public AttendanceProcessingService(
            IServiceScopeFactory scopeFactory,
            ILogger<AttendanceProcessingService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Process immediately when the application starts.
                    await ProcessTodayAsync();

                    // Run again every minute so newly approved leave
                    // is picked up without waiting until midnight.
                    await Task.Delay(
                        TimeSpan.FromMinutes(1),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Attendance processing failed.");

                    // Prevent a tight retry loop if something goes wrong.
                    try
                    {
                        await Task.Delay(
                            TimeSpan.FromMinutes(1),
                            stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        public async Task ProcessTodayAsync()
        {
            using var scope = _scopeFactory.CreateScope();

            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            // Current calendar date in India.
            var todayIndia = DateTime.UtcNow
                .AddHours(5)
                .AddMinutes(30)
                .Date;

            // Same UTC representation used by Attendance.AttendanceDate.
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

            // Do not process weekly offs.
            if (WorkCalendar.IsWeeklyOff(todayIndia))
            {
                return;
            }

            // Companies that have declared today a holiday.
            var holidayCompanyIds = await db.Holidays
                .AsNoTracking()
                .Where(h => h.HolidayDate == todayIndia)
                .Select(h => h.CompanyId)
                .ToListAsync();

            // Approved full-day leave covering today's Indian calendar date.
            var approvedLeaves = await db.Leaves
                .AsNoTracking()
                .Include(l => l.Employee).ThenInclude(e => e!.User)
                .Where(l =>
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate.Date <= todayIndia &&
                    l.ToDate.Date >= todayIndia &&
                    l.TotalDays >= 1)
                .ToListAsync();

            if (approvedLeaves.Count == 0)
                return;

            // One query for today's existing attendance rows
            // (instead of one query per leave).
            var employeeIds = approvedLeaves
                .Select(l => l.EmployeeId)
                .Distinct()
                .ToList();

            var alreadyRecorded = (await db.Attendances
                .AsNoTracking()
                .Where(a =>
                    a.AttendanceDate == attendanceDate &&
                    employeeIds.Contains(a.EmployeeId))
                .Select(a => a.EmployeeId)
                .ToListAsync())
                .ToHashSet();

            foreach (var leave in approvedLeaves)
            {
                // Skip employees whose company has a holiday today.
                var companyId = leave.Employee?.User?.CompanyId;
                if (companyId.HasValue &&
                    holidayCompanyIds.Contains(companyId.Value))
                    continue;

                if (alreadyRecorded.Contains(leave.EmployeeId))
                    continue;

                db.Attendances.Add(new Attendance
                {
                    EmployeeId = leave.EmployeeId,
                    AttendanceDate = attendanceDate,
                    Status = "On Leave",
                    Source = "System",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

                try
                {
                    await db.SaveChangesAsync();
                    alreadyRecorded.Add(leave.EmployeeId);
                }
                catch (DbUpdateException ex)
                {
                    // The employee checked in (or another instance inserted
                    // the row) at the same moment. Skip this employee only;
                    // the others in this run are unaffected.
                    db.ChangeTracker.Clear();

                    _logger.LogInformation(
                        ex,
                        "Skipped system attendance row for employee {EmployeeId}: row already exists.",
                        leave.EmployeeId);
                }
            }
        }
    }
}