namespace ExpenseManagement.Helpers
{
    /// <summary>
    /// Extension methods to convert UTC DateTimes to IST (India Standard Time, UTC+5:30)
    /// for display in Razor views. All dates are stored as UTC in the database;
    /// call .ToIst() before .ToString(...) to show correct local time.
    /// Usage in views:
    ///   Non-nullable: @date.ToIst().ToString("dd MMM yyyy, hh:mm tt")
    ///   Nullable:     @date?.ToIst()?.ToString("dd MMM yyyy, hh:mm tt")
    /// </summary>
    public static class DateHelper
    {
        private static readonly TimeZoneInfo Ist =
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        /// <summary>Converts a UTC DateTime to IST.</summary>
        public static DateTime ToIst(this DateTime utc)
        {
            var utcTime = utc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
                : utc.ToUniversalTime();
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, Ist);
        }

        /// <summary>Converts a nullable UTC DateTime to IST. Returns null if input is null.</summary>
        public static DateTime? ToIst(this DateTime? utc)
            => utc.HasValue ? (DateTime?)utc.Value.ToIst() : null;
    }
}
