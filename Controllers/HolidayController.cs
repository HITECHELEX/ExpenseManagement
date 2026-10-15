using ExpenseManagement.Data;
using ExpenseManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagement.Controllers
{
    /// <summary>
    /// Company holiday calendar. Holidays are excluded from working days
    /// in leave day counts and payroll.
    /// </summary>
    [Authorize(Roles = "HR")]
    public class HolidayController : Controller
    {
        private readonly AppDbContext _db;

        public HolidayController(AppDbContext db)
        {
            _db = db;
        }

        private int CurrentCompanyId =>
            int.Parse(User.FindFirst("CompanyId")?.Value ?? "0");

        private int CurrentUserId =>
            int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");

        [HttpGet]
        public async Task<IActionResult> Index(int? year)
        {
            var todayIndia = DateTime.UtcNow.AddHours(5).AddMinutes(30);
            var selectedYear = year ?? todayIndia.Year;

            if (selectedYear < 2000 || selectedYear > 2100)
                return BadRequest();

            var start = new DateTime(selectedYear, 1, 1);
            var end = start.AddYears(1);

            var holidays = await _db.Holidays
                .AsNoTracking()
                .Where(h =>
                    h.CompanyId == CurrentCompanyId &&
                    h.HolidayDate >= start &&
                    h.HolidayDate < end)
                .OrderBy(h => h.HolidayDate)
                .ToListAsync();

            ViewBag.Year = selectedYear;
            return View(holidays);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DateTime holidayDate, string? name)
        {
            name = name?.Trim();

            if (holidayDate == default || string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Holiday date and name are required.";
                return RedirectToAction(nameof(Index));
            }

            if (name.Length > 150)
            {
                TempData["Error"] = "Holiday name cannot exceed 150 characters.";
                return RedirectToAction(nameof(Index), new { year = holidayDate.Year });
            }

            var date = holidayDate.Date;

            var exists = await _db.Holidays.AnyAsync(h =>
                h.CompanyId == CurrentCompanyId &&
                h.HolidayDate == date);

            if (exists)
            {
                TempData["Error"] = "A holiday already exists on that date.";
                return RedirectToAction(nameof(Index), new { year = date.Year });
            }

            var holiday = new Holiday
            {
                CompanyId = CurrentCompanyId,
                HolidayDate = date,
                Name = name,
                CreatedAt = DateTime.UtcNow
            };

            _db.Holidays.Add(holiday);

            try
            {
                await _db.SaveChangesAsync();

                _db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = CurrentCompanyId,
                    UserId = CurrentUserId,
                    Action = "Create",
                    EntityName = "Holiday",
                    EntityId = holiday.Id,
                    Description = $"Added holiday {date:dd-MMM-yyyy}: {name}",
                    CreatedAt = DateTime.UtcNow
                });

                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = "A holiday already exists on that date.";
                return RedirectToAction(nameof(Index), new { year = date.Year });
            }

            TempData["Success"] = "Holiday added.";
            return RedirectToAction(nameof(Index), new { year = date.Year });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var holiday = await _db.Holidays.FirstOrDefaultAsync(h =>
                h.Id == id &&
                h.CompanyId == CurrentCompanyId);

            if (holiday == null)
                return NotFound();

            var year = holiday.HolidayDate.Year;

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = CurrentCompanyId,
                UserId = CurrentUserId,
                Action = "Delete",
                EntityName = "Holiday",
                EntityId = holiday.Id,
                Description = $"Removed holiday {holiday.HolidayDate:dd-MMM-yyyy}: {holiday.Name}",
                CreatedAt = DateTime.UtcNow
            });

            _db.Holidays.Remove(holiday);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Holiday removed.";
            return RedirectToAction(nameof(Index), new { year });
        }
    }
}