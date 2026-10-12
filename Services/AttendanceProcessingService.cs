using ExpenseManagement.Data;
using ExpenseManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseManagement.Services
{
    public class AttendanceProcessingService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public AttendanceProcessingService(
            IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(
    CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // --------------------------------------------------------
                    // Process immediately when the application starts.
                    // --------------------------------------------------------

                    await ProcessTodayAsync();

                    // --------------------------------------------------------
                    // Run again every minute.
                    // This ensures newly approved leave is picked up
                    // without waiting until midnight.
                    // --------------------------------------------------------

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
                    Console.WriteLine(
                        $"Attendance processing error: {ex}");

                    // Prevent a tight retry loop if something goes wrong.
                    await Task.Delay(
                        TimeSpan.FromMinutes(1),
                        stoppingToken);
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

            // Convert today's Indian calendar date to the
            // same UTC representation used by Attendance.AttendanceDate.
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
            // ------------------------------------------------------------
            // Do not process weekly offs / holidays.
            // ------------------------------------------------------------

            if (!IsWorkingDay(todayIndia))
            {
                return;
            }

            // ------------------------------------------------------------
            // Find approved full-day leave applications
            // covering today's Indian calendar date.
            // ------------------------------------------------------------

            var approvedLeaves = await db.Leaves
                .AsNoTracking()
                .Where(l =>
                    l.Status == "Approved" &&
                    l.CancelledOn == null &&
                    l.FromDate.Date <= todayIndia &&
                    l.ToDate.Date >= todayIndia &&
                    l.TotalDays >= 1)
                .ToListAsync();

            foreach (var leave in approvedLeaves)
            {
                // Check whether today's Attendance record
                // already exists for this employee.
                var attendanceExists = await db.Attendances
                    .AnyAsync(a =>
                        a.EmployeeId == leave.EmployeeId &&
                        a.AttendanceDate == attendanceDate);

                if (attendanceExists)
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

                db.Attendances.Add(attendance);
            }

            await db.SaveChangesAsync();
        }

        private static bool IsWorkingDay(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Sunday)
                return false;

            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                var occurrence =
                    ((date.Day - 1) / 7) + 1;

                if (occurrence == 2 || occurrence == 4)
                    return false;
            }

            return true;
        }
    }
}