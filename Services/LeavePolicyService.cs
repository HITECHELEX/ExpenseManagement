using System.Text.Json;
using ExpenseManagement.Data;
using Microsoft.EntityFrameworkCore;

namespace ExpenseManagement.Services
{
    /// <summary>
    /// Per-leave-type rule. Defaults: every leave type draws from the
    /// annual paid-leave pool and is paid. Days beyond the pool are unpaid.
    /// </summary>
    public sealed class LeaveTypeRule
    {
        // true  = days are deducted from the annual paid-leave pool
        public bool UsesAnnualBalance { get; set; } = true;

        // false = approved days are treated as loss of pay in payroll
        public bool IsPaid { get; set; } = true;
    }

    public sealed class LeavePolicySettings
    {
        public decimal BaseAnnualPaidLeave { get; set; } = LeavePolicyService.DefaultAnnualPaidLeave;
        public decimal LongServiceAnnualPaidLeave { get; set; } = LeavePolicyService.DefaultLongServiceAnnualPaidLeave;
        public int LongServiceAfterYears { get; set; } = LeavePolicyService.DefaultLongServiceAfterYears;

        public Dictionary<string, string> LeaveTypes { get; set; }
            = new(StringComparer.OrdinalIgnoreCase)
            {
                ["CL"] = "Casual Leave",
                ["SL"] = "Sick Leave",
                ["C/Off"] = "Compensatory Off",
                ["Paternity"] = "Paternity Leave",
                ["Maternity"] = "Maternity Leave",
                ["Medical"] = "Medical Leave"
            };

        // Optional per-type overrides, keyed by leave type code.
        public Dictionary<string, LeaveTypeRule> TypeRules { get; set; }
            = new(StringComparer.OrdinalIgnoreCase);

        public LeaveTypeRule GetRule(string? leaveType)
        {
            if (!string.IsNullOrWhiteSpace(leaveType) &&
                TypeRules.TryGetValue(leaveType.Trim(), out var rule))
            {
                return rule;
            }

            return new LeaveTypeRule();
        }
    }

    public sealed class LeavePolicyService
    {
        public const decimal DefaultAnnualPaidLeave = 10m;
        public const decimal DefaultLongServiceAnnualPaidLeave = 15m;
        public const int DefaultLongServiceAfterYears = 5;

        private readonly AppDbContext _db;
        public LeavePolicyService(AppDbContext db) => _db = db;

        public async Task<LeavePolicySettings> GetAsync(int companyId)
        {
            var now = DateTime.UtcNow;

            var configuration = await _db.HRMConfigurations
                .AsNoTracking()
                .Where(c =>
                    c.CompanyId == companyId &&
                    c.ConfigurationType == "LeavePolicy" &&
                    c.ConfigurationCode == "DEFAULT" &&
                    c.IsActive &&
                    c.EffectiveFrom <= now &&
                    (c.EffectiveTo == null || c.EffectiveTo >= now))
                .OrderByDescending(c => c.Version)
                .FirstOrDefaultAsync();

            if (configuration == null ||
                string.IsNullOrWhiteSpace(configuration.ConfigurationJson))
                return new LeavePolicySettings();

            try
            {
                var policy = JsonSerializer.Deserialize<LeavePolicySettings>(
                    configuration.ConfigurationJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (policy == null) return new LeavePolicySettings();

                policy.LeaveTypes =
                    policy.LeaveTypes != null && policy.LeaveTypes.Count > 0
                        ? new Dictionary<string, string>(policy.LeaveTypes, StringComparer.OrdinalIgnoreCase)
                        : new LeavePolicySettings().LeaveTypes;

                policy.TypeRules =
                    policy.TypeRules != null
                        ? new Dictionary<string, LeaveTypeRule>(policy.TypeRules, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, LeaveTypeRule>(StringComparer.OrdinalIgnoreCase);

                if (policy.BaseAnnualPaidLeave <= 0) policy.BaseAnnualPaidLeave = DefaultAnnualPaidLeave;
                if (policy.LongServiceAnnualPaidLeave <= 0) policy.LongServiceAnnualPaidLeave = DefaultLongServiceAnnualPaidLeave;
                if (policy.LongServiceAfterYears < 0) policy.LongServiceAfterYears = DefaultLongServiceAfterYears;

                return policy;
            }
            catch
            {
                return new LeavePolicySettings();
            }
        }

        public static int CalculateCompletedYears(DateTime? joiningDate, DateTime asOfDate)
        {
            if (!joiningDate.HasValue) return 0;
            var joined = joiningDate.Value.Date;
            var today = asOfDate.Date;
            if (joined > today) return 0;

            var years = today.Year - joined.Year;
            if (today < joined.AddYears(years)) years--;
            return Math.Max(0, years);
        }

        public static decimal GetAnnualEntitlement(
            DateTime? joiningDate, LeavePolicySettings policy, DateTime asOfDate)
        {
            return CalculateCompletedYears(joiningDate, asOfDate) > policy.LongServiceAfterYears
                ? policy.LongServiceAnnualPaidLeave
                : policy.BaseAnnualPaidLeave;
        }
    }
}