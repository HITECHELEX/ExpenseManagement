using ExpenseManagement.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagement.Services
{
    public static class WorkCalendar
    {
        // Sunday, 2nd and 4th Saturday (existing rule, now in one place)
        public static bool IsWeeklyOff(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Sunday) return true;
            if (date.DayOfWeek == DayOfWeek.Saturday)
            {
                var occurrence = ((date.Day - 1) / 7) + 1;
                return occurrence == 2 || occurrence == 4;
            }
            return false;
        }

        public static bool IsNonWorkingDay(DateTime date, ISet<DateTime> holidays)
            => IsWeeklyOff(date) || holidays.Contains(date.Date);

        public static async Task<HashSet<DateTime>> LoadHolidaysAsync(
            AppDbContext db, int companyId, DateTime fromInclusive, DateTime toInclusive)
        {
            var from = fromInclusive.Date;
            var to = toInclusive.Date;

            var dates = await db.Holidays
                .AsNoTracking()
                .Where(h => h.CompanyId == companyId &&
                            h.HolidayDate >= from &&
                            h.HolidayDate <= to)
                .Select(h => h.HolidayDate)
                .ToListAsync();

            return dates.Select(d => d.Date).ToHashSet();
        }
    }
}