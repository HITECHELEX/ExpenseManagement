using ExpenseManagement.Models;

namespace ExpenseManagement.Services
{
    public sealed record PayrollDaysResult(decimal WorkingDays, decimal PaidDays, decimal Lop);

    public static class PayrollDaysCalculator
    {
        public static PayrollDaysResult Calculate(
            Employee employee,
            DateTime monthStart,
            DateTime lastDateToEvaluate,
            IReadOnlyCollection<Attendance> attendance,
            IReadOnlyCollection<Leave> approvedLeaves,        // overlapping this month
            IReadOnlyCollection<Leave> approvedLeavesForYear, // FromDate in the leave year
            ISet<DateTime> holidays,                          // must cover Jan 1 .. month end
            decimal annualPaidLeaveEntitlement,
            Func<DateTime, DateTime> toAttendanceDate,
            LeavePolicySettings policy)
        {
            // Paid leave already consumed from the annual pool earlier in the year.
            // Leave types that do not use the pool (or are unpaid) never consume it.
            decimal paidLeaveUsed = 0m;
            foreach (var leave in approvedLeavesForYear)
            {
                var rule = policy.GetRule(leave.LeaveType);
                if (!rule.IsPaid || !rule.UsesAnnualBalance) continue;

                if (leave.FromDate.Date >= monthStart) continue;

                var leaveStart = leave.FromDate.Date;
                var leaveEnd = leave.ToDate.Date;
                var effectiveEnd = leaveEnd < monthStart ? leaveEnd : monthStart.AddDays(-1);
                if (effectiveEnd < leaveStart) continue;

                decimal used = 0m;
                for (var d = leaveStart; d <= effectiveEnd; d = d.AddDays(1))
                    if (!WorkCalendar.IsNonWorkingDay(d, holidays)) used += 1m;

                if (leave.TotalDays == 0.5m && leaveStart < monthStart) used = 0.5m;
                paidLeaveUsed += used;
            }

            decimal workingDays = 0m, paidDays = 0m, lop = 0m;

            void ApplyLeaveDay(Leave leave)
            {
                var value = leave.TotalDays == 0.5m ? 0.5m : 1m;
                var rule = policy.GetRule(leave.LeaveType);

                // Unpaid leave type: loss of pay.
                if (!rule.IsPaid)
                {
                    lop += value;
                    return;
                }

                // Paid leave type outside the annual pool (e.g. maternity):
                // fully paid, limits are enforced when the leave is approved.
                if (!rule.UsesAnnualBalance)
                {
                    paidDays += value;
                    return;
                }

                var remaining = Math.Max(0m, annualPaidLeaveEntitlement - paidLeaveUsed);
                var paid = Math.Min(value, remaining);
                paidDays += paid;
                lop += value - paid;
                paidLeaveUsed += paid;
            }

            var joining = employee.JoiningDate?.Date;
            var exit = employee.ExitDate?.Date;

            for (var day = monthStart; day <= lastDateToEvaluate; day = day.AddDays(1))
            {
                if (WorkCalendar.IsNonWorkingDay(day, holidays)) continue;

                workingDays++; // divisor = all working days in the window

                // Not employed on this day: no pay and no LOP
                if ((joining.HasValue && day < joining.Value) ||
                    (exit.HasValue && day > exit.Value))
                    continue;

                var attDate = toAttendanceDate(day);
                var dayAttendance = attendance.FirstOrDefault(a => a.AttendanceDate == attDate);
                var leaveToday = approvedLeaves.FirstOrDefault(
                    l => l.FromDate.Date <= day && l.ToDate.Date >= day);

                if (dayAttendance != null)
                {
                    var s = dayAttendance.Status;
                    if (s == "Present") { paidDays += 1m; continue; }
                    if (s == "Half Day") { paidDays += 0.5m; continue; }
                    if (s == "No Punch-In" || s == "No Out-Punch") { paidDays += 1m; continue; }
                    if (s == "On Leave")
                    {
                        if (leaveToday != null) ApplyLeaveDay(leaveToday); else lop += 1m;
                        continue;
                    }
                }

                if (leaveToday != null) ApplyLeaveDay(leaveToday); else lop += 1m;
            }

            return new PayrollDaysResult(workingDays, paidDays, lop);
        }
    }
}