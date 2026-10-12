using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.ViewModels;
using ExpenseManagement.Helpers;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Management")]
    public class ManagementController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        // Directors now loaded from DB (PortalProfiles) — no longer hardcoded

        public ManagementController(AppDbContext db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string? SelectedDirector => HttpContext.Session.GetString("DirectorName");

        // ── Force director selection before any Management page ──────────────
        // Also validates the stored profile ID is still active in DB —
        // handles deleted directors and stale sessions across logins.
        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            var action = context.RouteData.Values["action"]?.ToString();
            if (action != null && (
                action.Equals("SelectDirector",  StringComparison.OrdinalIgnoreCase) ||
                action.Equals("SwitchDirector",  StringComparison.OrdinalIgnoreCase) ||
                action.Equals("ManageDirectors", StringComparison.OrdinalIgnoreCase) ||
                action.Equals("AddDirector",     StringComparison.OrdinalIgnoreCase) ||
                action.Equals("DeleteDirector",  StringComparison.OrdinalIgnoreCase)))
                return;

            if (string.IsNullOrEmpty(SelectedDirector))
            {
                context.Result = RedirectToAction("SelectDirector");
                return;
            }

            // Validate stored profile ID is still active in DB
            var storedIdStr = HttpContext.Session.GetString("DirectorProfileId");
            if (int.TryParse(storedIdStr, out var storedId))
            {
                var valid = _db.PortalProfiles.Any(p =>
                    p.Id == storedId && p.Role == UserRole.Management && p.IsActive);

                if (!valid)
                {
                    HttpContext.Session.Remove("DirectorName");
                    HttpContext.Session.Remove("DirectorProfileId");
                    context.Result = RedirectToAction("SelectDirector");
                }
            }
            else
            {
                HttpContext.Session.Remove("DirectorName");
                context.Result = RedirectToAction("SelectDirector");
            }
        }

        // ── Director Selection ────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> SelectDirector()
        {
            if (!string.IsNullOrEmpty(SelectedDirector))
                return RedirectToAction("Dashboard");

            var profiles = await _db.PortalProfiles
                .Where(p => p.Role == UserRole.Management && p.IsActive)
                .OrderBy(p => p.Name).ToListAsync();

            ViewBag.Profiles = profiles;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectDirector(int profileId)
        {
            var profile = await _db.PortalProfiles
                .FirstOrDefaultAsync(p => p.Id == profileId && p.Role == UserRole.Management && p.IsActive);

            if (profile == null)
            {
                ModelState.AddModelError("", "Please select a valid director.");
                var profiles = await _db.PortalProfiles
                    .Where(p => p.Role == UserRole.Management && p.IsActive)
                    .OrderBy(p => p.Name).ToListAsync();
                ViewBag.Profiles = profiles;
                return View();
            }

            HttpContext.Session.SetString("DirectorName", profile.Name);
            HttpContext.Session.SetString("DirectorProfileId", profile.Id.ToString());
            return RedirectToAction("Dashboard", "HRMManagement");
        }

        // ── Manage Management Profiles ───────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ManageDirectors()
        {
            var profiles = await _db.PortalProfiles
                .Where(p => p.Role == UserRole.Management)
                .OrderBy(p => p.Name).ToListAsync();
            return View(profiles);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDirector(string name)
        {
            name = name?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            { TempData["Error"] = "Name cannot be empty."; return RedirectToAction("ManageDirectors"); }

            var exists = await _db.PortalProfiles.AnyAsync(p => p.Name == name && p.Role == UserRole.Management);
            if (exists)
            { TempData["Error"] = $"'{name}' already exists."; return RedirectToAction("ManageDirectors"); }

            _db.PortalProfiles.Add(new PortalProfile
            {
                Name = name, Role = UserRole.Management,
                CompanyId = null, IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Director '{name}' added.";
            return RedirectToAction("ManageDirectors");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDirector(int id)
        {
            var profile = await _db.PortalProfiles
                .FirstOrDefaultAsync(p => p.Id == id && p.Role == UserRole.Management);
            if (profile != null)
            {
                _db.PortalProfiles.Remove(profile);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Director '{profile.Name}' removed.";
                if (HttpContext.Session.GetString("DirectorName") == profile.Name)
                {
                    HttpContext.Session.Remove("DirectorName");
                    HttpContext.Session.Remove("DirectorProfileId");
                }
            }
            return RedirectToAction("ManageDirectors");
        }

        // ── Switch Director (clears session, shows selection again) ──────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SwitchDirector()
        {
            HttpContext.Session.Remove("DirectorName");
            return RedirectToAction("SelectDirector");
        }

        /// <summary>Returns employee IDs belonging to a company (or all if companyId is null).</summary>
        private async Task<List<int>> GetEmployeeIds(int? companyId)
        {
            return await _db.Users
                .Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
                .Select(u => u.Id).ToListAsync();
        }

        /// <summary>Populates ViewBag with companies list and selected company — used by all Management pages.</summary>
        private async Task PopulateCompanyFilter(int? companyId)
        {
            ViewBag.Companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            ViewBag.SelectedCompanyId = companyId;
        }

        public async Task<IActionResult> Dashboard(int? companyId)
        {
            companyId ??= 1;
            await PopulateCompanyFilter(companyId);
            var companies = await _db.Companies.OrderBy(c => c.Name).ToListAsync();
            var employeeIds = await GetEmployeeIds(companyId);
            var selectedCompany = companyId.HasValue ? companies.FirstOrDefault(c => c.Id == companyId) : null;

            var unread = await _db.Notifications
                .CountAsync(n => n.UserId == CurrentUserId && !n.IsRead);

            var vm = new ManagementDashboardVM
            {
                TotalEmployees = employeeIds.Count,
                TotalBudgetAllocated = await _db.Budgets.Where(b => employeeIds.Contains(b.EmployeeId)).SumAsync(b => b.TotalAmount),
                TotalSpent = await _db.Budgets.Where(b => employeeIds.Contains(b.EmployeeId)).SumAsync(b => b.SpentAmount),
                ReportsForReview = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.ForwardedToManagement && employeeIds.Contains(r.EmployeeId)),
                ApprovedReports = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.ApprovedByManagement && employeeIds.Contains(r.EmployeeId)),
                RejectedReports = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.RejectedByManagement && employeeIds.Contains(r.EmployeeId)),
                RecentReports = await _db.ExpenseReports
                    .AsNoTracking()
                    .Include(r => r.Employee)
                    .Include(r => r.ExpenseClaims)
                    .Where(r => r.Status == ReportStatus.ForwardedToManagement && employeeIds.Contains(r.EmployeeId))
                    .OrderByDescending(r => r.AccountTeamVerifiedOn).Take(5).ToListAsync(),
                RecentBudgets = await _db.Budgets
                    .AsNoTracking()
                    .Include(b => b.Employee)
                    .Where(b => employeeIds.Contains(b.EmployeeId))
                    .OrderByDescending(b => b.AllocatedOn).Take(5).ToListAsync(),
                UnreadNotifications = unread,
                SelectedCompanyId = companyId,
                SelectedCompanyName = selectedCompany?.Name ?? "All Companies",
                Companies = companies
            };
            return View(vm);
        }

        public async Task<IActionResult> Reports(string? status, int? companyId, int? employeeId)
        {
            companyId ??= 1;
            await PopulateCompanyFilter(companyId);
            var employeeIds = await GetEmployeeIds(companyId);

            // Fetch employees for this company to populate the filter dropdown
            var employees = await _db.Users
                .Where(u => u.Role == UserRole.Employee && employeeIds.Contains(u.Id))
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName })
                .ToListAsync();
            ViewBag.Employees = employees;
            ViewBag.SelectedEmployeeId = employeeId;

            var query = _db.ExpenseReports
                .Include(r => r.Employee).ThenInclude(e => e!.Company)
                .Include(r => r.Budget)
                .Include(r => r.AccountTeamVerifiedBy)
                .Where(r => employeeIds.Contains(r.EmployeeId))
                .AsQueryable();

            // Apply employee filter if selected
            if (employeeId.HasValue)
                query = query.Where(r => r.EmployeeId == employeeId.Value);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var s))
                query = query.Where(r => r.Status == s);
            else
                query = query.Where(r => r.Status == ReportStatus.ForwardedToManagement);

            ViewBag.CurrentStatus = status ?? "ForwardedToManagement";
            ViewBag.SelectedCompanyId = companyId;
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
            report.ManagementReviewedOn = DateTime.UtcNow;
            report.DirectorName = SelectedDirector ?? User.Identity?.Name;

            if (model.IsApproved && report.Budget != null)
            {
                // On approval, remaining balance is officially returned.
                // Positive = unused balance returned to employee.
                // Negative = overspend, deducted from next allocation.
                report.Budget.ReturnedBalance += report.RemainingBalanceAtSubmission;
            }

            // Notify account team of the SAME company only
            var atUsers = await _db.Users
                .Where(u => u.Role == UserRole.AccountTeam && u.CompanyId == report.Employee!.CompanyId)
                .ToListAsync();
            var directorLabel = report.DirectorName ?? "Management";
            foreach (var at in atUsers)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = at.Id,
                    Message = $"Report '{report.ReportTitle}' has been {(model.IsApproved ? "APPROVED" : "REJECTED")} by {directorLabel}.",
                    Link = $"/AccountTeam/ReportDetail/{report.Id}",
                    Icon = model.IsApproved ? "check-circle" : "times-circle",
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Notify employee
            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' has been {(model.IsApproved ? "APPROVED" : "REJECTED")} by {directorLabel}. {model.Feedback}",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = model.IsApproved ? "check-circle" : "times-circle",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Report {(model.IsApproved ? "approved" : "rejected")} successfully.";
            return RedirectToAction("Reports", new { companyId = report.Employee?.CompanyId });
        }

        public async Task<IActionResult> Overview(int? companyId)
        {
            companyId ??= 1;
            await PopulateCompanyFilter(companyId);
            var employees = await _db.Users
                .Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
                .Include(u => u.Company)
                .Include(u => u.Budgets)
                .Include(u => u.ExpenseClaims)
                .ToListAsync();
            ViewBag.SelectedCompanyId = companyId;
            return View(employees);
        }

        public async Task<IActionResult> BudgetOverview(int? companyId, string? month)
        {
            companyId ??= 1;
            await PopulateCompanyFilter(companyId);

            var allBudgets = await _db.Budgets
                .AsNoTracking()
                .Include(b => b.Employee).ThenInclude(e => e!.Company)
                .Include(b => b.AllocatedBy)
                .Where(b => companyId == null || b.Employee!.CompanyId == companyId)
                .OrderByDescending(b => b.AllocatedOn).ToListAsync();

            // Build month list from IST dates
            var availableMonths = allBudgets
                .Select(b => b.AllocatedOn.ToIst())
                .Select(d => new { Key = d.ToString("yyyy-MM"), Label = d.ToString("MMM yyyy") })
                .DistinctBy(m => m.Key)
                .OrderByDescending(m => m.Key)
                .ToList();

            // Filter by selected month (IST-aware)
            if (!string.IsNullOrEmpty(month) && DateTime.TryParseExact(month + "-01", "yyyy-MM-dd",
                    null, System.Globalization.DateTimeStyles.None, out var sel))
            {
                allBudgets = allBudgets
                    .Where(b => { var d = b.AllocatedOn.ToIst(); return d.Year == sel.Year && d.Month == sel.Month; })
                    .ToList();
            }

            ViewBag.SelectedCompanyId = companyId;
            ViewBag.SelectedMonth     = month ?? "";
            ViewBag.AvailableMonths   = availableMonths;
            ViewBag.TotalAllocated    = allBudgets.Sum(b => b.TotalAmount);
            ViewBag.TotalSpent        = allBudgets.Sum(b => b.SpentAmount);
            ViewBag.TotalRemaining    = allBudgets.Sum(b => b.RemainingBalance);
            return View(allBudgets);
        }

        public async Task<IActionResult> AllClaims(int? companyId)
        {
            companyId ??= 1;
            await PopulateCompanyFilter(companyId);
            var claims = await _db.ExpenseClaims
                .Include(c => c.Employee).ThenInclude(e => e!.Company)
                .Include(c => c.Budget).Include(c => c.ReviewedBy)
                .Where(c => companyId == null || c.Employee!.CompanyId == companyId)
                .OrderByDescending(c => c.SubmittedOn).ToListAsync();
            ViewBag.SelectedCompanyId = companyId;
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
