using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.ViewModels;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Management")]
    public class ManagementController : Controller
    {
        private readonly AppDbContext _db;
        public ManagementController(AppDbContext db) => _db = db;
        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public async Task<IActionResult> Dashboard()
        {
            var unread = await _db.Notifications
                .CountAsync(n => n.UserId == CurrentUserId && !n.IsRead);

            var vm = new ManagementDashboardVM
            {
                TotalEmployees = await _db.Users.CountAsync(u => u.Role == UserRole.Employee),
                TotalBudgetAllocated = await _db.Budgets.SumAsync(b => b.TotalAmount),
                TotalSpent = await _db.Budgets.SumAsync(b => b.SpentAmount),
                ReportsForReview = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.ForwardedToManagement),
                ApprovedReports = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.ApprovedByManagement),
                RejectedReports = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.RejectedByManagement),
                RecentReports = await _db.ExpenseReports
                    .Include(r => r.Employee)
                    .Include(r => r.ExpenseClaims)
                    .Where(r => r.Status == ReportStatus.ForwardedToManagement)
                    .OrderByDescending(r => r.AccountTeamVerifiedOn).Take(5).ToListAsync(),
                RecentBudgets = await _db.Budgets
                    .Include(b => b.Employee)
                    .OrderByDescending(b => b.AllocatedOn).Take(5).ToListAsync(),
                UnreadNotifications = unread
            };
            return View(vm);
        }

        public async Task<IActionResult> Reports(string? status)
        {
            var query = _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.Budget)
                .Include(r => r.AccountTeamVerifiedBy).AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var s))
                query = query.Where(r => r.Status == s);
            else
                query = query.Where(r => r.Status == ReportStatus.ForwardedToManagement);

            ViewBag.CurrentStatus = status ?? "ForwardedToManagement";
            return View(await query.OrderByDescending(r => r.CreatedOn).ToListAsync());
        }

        public async Task<IActionResult> ReportDetail(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Include(r => r.AccountTeamVerifiedBy)
                .Include(r => r.ManagementReviewedBy)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null) return NotFound();
            return View(report);
        }

        [HttpPost]
        public async Task<IActionResult> ReviewReport(ReviewReportVM model)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee)
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == model.ReportId && r.Status == ReportStatus.ForwardedToManagement);

            if (report == null) return NotFound();

            report.Status = model.IsApproved ? ReportStatus.ApprovedByManagement : ReportStatus.RejectedByManagement;
            report.ManagementNotes = model.Notes;
            report.ManagementFeedback = model.Feedback;
            report.ManagementReviewedById = CurrentUserId;
            report.ManagementReviewedOn = DateTime.Now;

            if (model.IsApproved && report.Budget != null)
            {
                // On approval, remaining balance is officially returned
                report.Budget.ReturnedBalance += report.RemainingBalanceAtSubmission;
            }

            // Notify account team
            var atUsers = await _db.Users.Where(u => u.Role == UserRole.AccountTeam).ToListAsync();
            foreach (var at in atUsers)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = at.Id,
                    Message = $"Report '{report.ReportTitle}' has been {(model.IsApproved ? "APPROVED" : "REJECTED")} by Management.",
                    Link = $"/AccountTeam/ReportDetail/{report.Id}",
                    Icon = model.IsApproved ? "check-circle" : "times-circle",
                    CreatedAt = DateTime.Now
                });
            }

            // Notify employee
            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' has been {(model.IsApproved ? "APPROVED" : "REJECTED")} by Management. {model.Feedback}",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = model.IsApproved ? "check-circle" : "times-circle",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Report {(model.IsApproved ? "approved" : "rejected")} successfully.";
            return RedirectToAction("Reports");
        }

        public async Task<IActionResult> Overview()
        {
            var employees = await _db.Users
                .Where(u => u.Role == UserRole.Employee)
                .Include(u => u.Budgets)
                .Include(u => u.ExpenseClaims)
                .ToListAsync();
            return View(employees);
        }

        public async Task<IActionResult> BudgetOverview()
        {
            var budgets = await _db.Budgets
                .Include(b => b.Employee).Include(b => b.AllocatedBy)
                .OrderByDescending(b => b.AllocatedOn).ToListAsync();
            return View(budgets);
        }

        public async Task<IActionResult> AllClaims()
        {
            var claims = await _db.ExpenseClaims
                .Include(c => c.Employee).Include(c => c.Budget).Include(c => c.ReviewedBy)
                .OrderByDescending(c => c.SubmittedOn).ToListAsync();
            return View(claims);
        }

        public async Task<IActionResult> Notifications()
        {
            var notifications = await _db.Notifications
                .Where(n => n.UserId == CurrentUserId)
                .OrderByDescending(n => n.CreatedAt).ToListAsync();
            notifications.ForEach(n => n.IsRead = true);
            await _db.SaveChangesAsync();
            return View(notifications);
        }
    }
}
