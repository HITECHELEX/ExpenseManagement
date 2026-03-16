using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.ViewModels;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Employee")]
    public class EmployeeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public EmployeeController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ─── DASHBOARD ────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            var userId = CurrentUserId;
            var employee = await _db.Users.FindAsync(userId);

            var allBudgets = await _db.Budgets
                .Where(b => b.EmployeeId == userId && b.IsActive)
                .OrderByDescending(b => b.AllocatedOn).ToListAsync();

            var activeBudgets = allBudgets.Where(b => !b.IsLocked).ToList();

            var claims = await _db.ExpenseClaims
                .Where(c => c.EmployeeId == userId)
                .OrderByDescending(c => c.SubmittedOn)
                .Take(10).ToListAsync();

            var draftReports = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Where(r => r.EmployeeId == userId && r.Status == ReportStatus.Draft)
                .ToListAsync();

            var unread = await _db.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            // Global wallet balance = sum of all active+unlocked budgets' remaining
            decimal walletBalance = activeBudgets.Sum(b => b.RemainingBalance);

            var vm = new EmployeeDashboardVM
            {
                Employee = employee!,
                Budgets  = allBudgets,
                DraftReports   = draftReports,
                RecentClaims   = claims,
                TotalAllocated = activeBudgets.Sum(b => b.TotalAmount),
                TotalSpent     = activeBudgets.Sum(b => b.SpentAmount),
                TotalBalance   = walletBalance,
                PendingCount   = claims.Count(c => c.Status == ExpenseStatus.Pending),
                ApprovedCount  = claims.Count(c => c.Status == ExpenseStatus.Approved),
                DeclinedCount  = claims.Count(c => c.Status == ExpenseStatus.Declined),
                UnreadNotifications = unread
            };

            return View(vm);
        }

        // ─── MY BUDGETS ───────────────────────────────────────
        public async Task<IActionResult> MyBudgets()
        {
            var budgets = await _db.Budgets
                .Include(b => b.AllocatedBy)
                .Where(b => b.EmployeeId == CurrentUserId)
                .OrderByDescending(b => b.AllocatedOn)
                .ToListAsync();
            return View(budgets);
        }

        // ─── CREATE DRAFT REPORT (wallet-based) ──────────────
        // Reports are created against the employee's global wallet balance
        // (sum of all active unlocked budgets). No budget selection needed.
        [HttpGet]
        public async Task<IActionResult> CreateDraftReport()
        {
            var userId = CurrentUserId;

            var activeBudgets = await _db.Budgets
                .Where(b => b.EmployeeId == userId && b.IsActive && !b.IsLocked)
                .OrderBy(b => b.AllocatedOn).ToListAsync();

            if (!activeBudgets.Any())
            {
                TempData["Error"] = "No active budget found. Ask the Account Team to allocate a budget first.";
                return RedirectToAction("Dashboard");
            }

            decimal walletBalance = activeBudgets.Sum(b => b.RemainingBalance);
            ViewBag.WalletBalance = walletBalance;
            ViewBag.PrimaryBudgetId = activeBudgets.First().Id;
            return View(new CreateReportVM { BudgetId = activeBudgets.First().Id });
        }

        [HttpPost]
        public async Task<IActionResult> CreateDraftReport(CreateReportVM model)
        {
            var userId = CurrentUserId;
            var activeBudgets = await _db.Budgets
                .Where(b => b.EmployeeId == userId && b.IsActive && !b.IsLocked)
                .OrderBy(b => b.AllocatedOn).ToListAsync();
            decimal walletBalance = activeBudgets.Sum(b => b.RemainingBalance);
            ViewBag.WalletBalance = walletBalance;
            ViewBag.PrimaryBudgetId = activeBudgets.FirstOrDefault()?.Id;

            if (!ModelState.IsValid) return View(model);

            if (!activeBudgets.Any())
            { TempData["Error"] = "No active budget found."; return RedirectToAction("Dashboard"); }

            var primaryBudget = activeBudgets.First();

            var report = new ExpenseReport
            {
                EmployeeId  = userId,
                BudgetId    = primaryBudget.Id,
                ReportTitle = model.ReportTitle,
                Summary     = model.Summary,
                TotalClaimed  = 0,
                TotalApproved = 0,
                Status    = ReportStatus.Draft,
                CreatedOn = DateTime.Now
            };

            _db.ExpenseReports.Add(report);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Draft report created. Add daily expenses then forward to Account Team.";
            return RedirectToAction("ReportDetail", new { id = report.Id });
        }

        // ─── EDIT DRAFT REPORT (title / summary) ─────────────
        [HttpGet]
        public async Task<IActionResult> EditReport(int id)
        {
            var report = await _db.ExpenseReports
                .FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == CurrentUserId && r.Status == ReportStatus.Draft);
            if (report == null) return NotFound();

            return View(new EditReportVM
            {
                ReportId    = report.Id,
                ReportTitle = report.ReportTitle,
                Summary     = report.Summary
            });
        }

        [HttpPost]
        public async Task<IActionResult> EditReport(EditReportVM model)
        {
            if (!ModelState.IsValid) return View(model);

            var report = await _db.ExpenseReports
                .FirstOrDefaultAsync(r => r.Id == model.ReportId && r.EmployeeId == CurrentUserId && r.Status == ReportStatus.Draft);
            if (report == null) return NotFound();

            report.ReportTitle = model.ReportTitle;
            report.Summary     = model.Summary;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Report details updated.";
            return RedirectToAction("ReportDetail", new { id = report.Id });
        }

        // ─── DELETE DRAFT REPORT ──────────────────────────────
        [HttpPost]
        public async Task<IActionResult> DeleteReport(int reportId)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.EmployeeId == CurrentUserId && r.Status == ReportStatus.Draft);
            if (report == null) return NotFound();

            // Restore all budget deductions this draft made
            if (report.Budget != null && report.ExpenseClaims.Any())
            {
                decimal total = report.ExpenseClaims.Sum(c => c.Amount);
                report.Budget.SpentAmount = Math.Max(0, report.Budget.SpentAmount - total);
            }

            // Delete receipt files
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            foreach (var claim in report.ExpenseClaims)
            {
                if (!string.IsNullOrEmpty(claim.ReceiptPath))
                {
                    var fp = Path.Combine(webRoot, claim.ReceiptPath.TrimStart('/'));
                    if (System.IO.File.Exists(fp)) System.IO.File.Delete(fp);
                }
            }

            _db.ExpenseClaims.RemoveRange(report.ExpenseClaims);
            _db.ExpenseReports.Remove(report);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Draft report deleted. Budget balance has been restored.";
            return RedirectToAction("MyReports");
        }

        // ─── ADD EXPENSE TO DRAFT ─────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AddExpense(int reportId)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.EmployeeId == CurrentUserId && r.Status == ReportStatus.Draft);
            if (report == null) return NotFound();

            ViewBag.Report = report;
            return View(new ExpenseClaimVM { BudgetId = report.BudgetId, ReportId = reportId });
        }

        [HttpPost]
        public async Task<IActionResult> AddExpense(ExpenseClaimVM model)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == model.ReportId && r.EmployeeId == CurrentUserId && r.Status == ReportStatus.Draft);
            if (report == null) return NotFound();
            ViewBag.Report = report;

            if (!ModelState.IsValid) return View(model);

            var budget = await _db.Budgets.FindAsync(model.BudgetId);
            if (budget == null || budget.EmployeeId != CurrentUserId)
            {
                ModelState.AddModelError("", "Invalid budget.");
                return View(model);
            }

            // ── OVERSPEND IS ALLOWED ──
            // Budget 5000, spend 5500 → balance = -500
            // On next allocation of 10000 → SpentAmount pre-filled 500 → net 9500

            string? receiptPath = null, receiptFileName = null;
            if (model.Receipt != null && model.Receipt.Length > 0)
            {
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".webp" };
                var ext = Path.GetExtension(model.Receipt.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                { ModelState.AddModelError("Receipt", "Only JPG, PNG, GIF, WEBP or PDF files allowed."); return View(model); }
                if (model.Receipt.Length > 10 * 1024 * 1024)
                { ModelState.AddModelError("Receipt", "File must be under 10MB."); return View(model); }

                var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var uploads = Path.Combine(webRoot, "uploads", "receipts");
                Directory.CreateDirectory(uploads);
                var fName = $"{Guid.NewGuid()}{ext}";
                using var fs = new FileStream(Path.Combine(uploads, fName), FileMode.Create);
                await model.Receipt.CopyToAsync(fs);
                receiptPath = $"/uploads/receipts/{fName}";
                receiptFileName = model.Receipt.FileName;
            }

            var claim = new ExpenseClaim
            {
                EmployeeId = CurrentUserId,
                BudgetId = model.BudgetId,
                ExpenseReportId = model.ReportId,
                Title = model.Title,
                Description = model.Description,
                Category = model.Category,
                Amount = model.Amount,
                ExpenseDate = model.ExpenseDate,
                ReceiptPath = receiptPath,
                ReceiptFileName = receiptFileName,
                Status = ExpenseStatus.Pending,
                SubmittedOn = DateTime.Now
            };

            _db.ExpenseClaims.Add(claim);
            budget.SpentAmount += model.Amount;   // CAN go negative
            report.TotalClaimed += model.Amount;

            await _db.SaveChangesAsync();

            decimal newBal = budget.RemainingBalance;
            string balNote = newBal >= 0
                ? $"Remaining budget: ₹{newBal:N2}"
                : $"⚠️ Over budget by ₹{Math.Abs(newBal):N2} — will be auto-deducted from next allocation.";

            TempData["Success"] = $"₹{model.Amount:N2} added. {balNote}";
            return RedirectToAction("ReportDetail", new { id = model.ReportId });
        }

        // ─── EDIT EXPENSE IN DRAFT ────────────────────────────
        [HttpGet]
        public async Task<IActionResult> EditExpense(int id)
        {
            var claim = await _db.ExpenseClaims
                .Include(c => c.ExpenseReport)
                .FirstOrDefaultAsync(c => c.Id == id && c.EmployeeId == CurrentUserId);
            if (claim == null) return NotFound();

            if (claim.ExpenseReport?.Status != ReportStatus.Draft)
            {
                TempData["Error"] = "Only expenses in draft reports can be edited.";
                return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
            }

            ViewBag.Report = await _db.ExpenseReports.Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == claim.ExpenseReportId);
            ViewBag.ExistingReceipt = claim.ReceiptPath;

            return View(new EditExpenseClaimVM
            {
                ClaimId = claim.Id,
                Title = claim.Title,
                Description = claim.Description,
                Category = claim.Category,
                Amount = claim.Amount,
                ExpenseDate = claim.ExpenseDate
            });
        }

        [HttpPost]
        public async Task<IActionResult> EditExpense(EditExpenseClaimVM model)
        {
            var claim = await _db.ExpenseClaims
                .Include(c => c.Budget)
                .Include(c => c.ExpenseReport)
                .FirstOrDefaultAsync(c => c.Id == model.ClaimId && c.EmployeeId == CurrentUserId);
            if (claim == null) return NotFound();

            if (claim.ExpenseReport?.Status != ReportStatus.Draft)
            {
                TempData["Error"] = "Only expenses in draft reports can be edited.";
                return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
            }

            ViewBag.Report = await _db.ExpenseReports.Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == claim.ExpenseReportId);
            ViewBag.ExistingReceipt = claim.ReceiptPath;

            if (!ModelState.IsValid) return View(model);

            // Adjust budget & report totals by the difference in amount
            decimal diff = model.Amount - claim.Amount;
            if (claim.Budget != null) claim.Budget.SpentAmount += diff;
            if (claim.ExpenseReport != null) claim.ExpenseReport.TotalClaimed = Math.Max(0, claim.ExpenseReport.TotalClaimed + diff);

            claim.Title = model.Title;
            claim.Description = model.Description;
            claim.Category = model.Category;
            claim.Amount = model.Amount;
            claim.ExpenseDate = model.ExpenseDate;

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");

            if (model.RemoveReceipt && !string.IsNullOrEmpty(claim.ReceiptPath))
            {
                var fp = Path.Combine(webRoot, claim.ReceiptPath.TrimStart('/'));
                if (System.IO.File.Exists(fp)) System.IO.File.Delete(fp);
                claim.ReceiptPath = null; claim.ReceiptFileName = null;
            }
            else if (model.Receipt != null && model.Receipt.Length > 0)
            {
                var allowed = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".webp" };
                var ext = Path.GetExtension(model.Receipt.FileName).ToLowerInvariant();
                if (!allowed.Contains(ext))
                { ModelState.AddModelError("Receipt", "Only JPG, PNG, GIF, WEBP or PDF allowed."); return View(model); }

                if (!string.IsNullOrEmpty(claim.ReceiptPath))
                {
                    var old = Path.Combine(webRoot, claim.ReceiptPath.TrimStart('/'));
                    if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
                }

                var uploads = Path.Combine(webRoot, "uploads", "receipts");
                Directory.CreateDirectory(uploads);
                var fName = $"{Guid.NewGuid()}{ext}";
                using var fs = new FileStream(Path.Combine(uploads, fName), FileMode.Create);
                await model.Receipt.CopyToAsync(fs);
                claim.ReceiptPath = $"/uploads/receipts/{fName}";
                claim.ReceiptFileName = model.Receipt.FileName;
            }

            await _db.SaveChangesAsync();

            decimal newBal = claim.Budget?.RemainingBalance ?? 0;
            string balNote = newBal >= 0 ? $"Budget balance: ₹{newBal:N2}" : $"⚠️ Over budget by ₹{Math.Abs(newBal):N2}";
            TempData["Success"] = $"Expense updated. {balNote}";
            return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
        }

        // ─── FORWARD REPORT TO ACCOUNT TEAM ──────────────────
        [HttpPost]
        public async Task<IActionResult> ForwardReport(int reportId, string? notes)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.EmployeeId == CurrentUserId && r.Status == ReportStatus.Draft);
            if (report == null) return NotFound();

            if (!report.ExpenseClaims.Any())
            {
                TempData["Error"] = "Cannot forward an empty report. Add at least one expense first.";
                return RedirectToAction("ReportDetail", new { id = reportId });
            }

            var budget = report.Budget!;
            decimal balAtSubmission = budget.RemainingBalance; // can be negative (overspent)

            report.RemainingBalanceAtSubmission = balAtSubmission;
            report.Status = ReportStatus.SubmittedToAccountTeam;
            report.Summary = notes ?? report.Summary;
            report.ForwardedToAccountTeamOn = DateTime.Now;
            report.TotalClaimed = report.ExpenseClaims.Sum(c => c.Amount);

            // Record overspend immediately so AT can see warning when allocating next budget
            if (balAtSubmission < 0)
                budget.OverspentAmount += Math.Abs(balAtSubmission);

            var currentUser = await _db.Users.FindAsync(CurrentUserId);
            var atMembers = await _db.Users.Where(u => u.Role == UserRole.AccountTeam).ToListAsync();

            string ovNote = balAtSubmission < 0
                ? $" ⚠️ Overspent ₹{Math.Abs(balAtSubmission):N2}"
                : $" Returning ₹{balAtSubmission:N2}";

            foreach (var at in atMembers)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = at.Id,
                    Message = $"Report '{report.ReportTitle}' from {currentUser!.FullName} submitted.{ovNote}",
                    Link = $"/AccountTeam/ReportDetail/{report.Id}",
                    Icon = "file-invoice",
                    CreatedAt = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = balAtSubmission >= 0
                ? $"Report forwarded. ₹{balAtSubmission:N2} unused balance will be returned after approval."
                : $"Report forwarded. Overspent by ₹{Math.Abs(balAtSubmission):N2} — auto-deducted from your next allocation.";

            return RedirectToAction("MyReports");
        }

        // ─── MY REPORTS ───────────────────────────────────────
        public async Task<IActionResult> MyReports()
        {
            var reports = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Where(r => r.EmployeeId == CurrentUserId)
                .OrderByDescending(r => r.CreatedOn)
                .ToListAsync();
            return View(reports);
        }

        public async Task<IActionResult> ReportDetail(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.Employee)
                .Include(r => r.ExpenseClaims)
                .Include(r => r.AccountTeamVerifiedBy)
                .Include(r => r.ManagementReviewedBy)
                .Include(r => r.TransferredToBudget)
                .FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == CurrentUserId);
            if (report == null) return NotFound();

            if (report.Status == ReportStatus.Draft)
            {
                var activeBudgets = await _db.Budgets
                    .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked)
                    .ToListAsync();
                ViewBag.WalletBalance = (decimal?)activeBudgets.Sum(b => b.RemainingBalance);
            }

            return View(report);
        }

        // ─── MY CLAIMS ────────────────────────────────────────
        public async Task<IActionResult> MyClaims(string? status, int? budgetId)
        {
            var query = _db.ExpenseClaims
                .Include(c => c.Budget)
                .Include(c => c.ReviewedBy)
                .Where(c => c.EmployeeId == CurrentUserId);

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ExpenseStatus>(status, out var s))
                query = query.Where(c => c.Status == s);
            if (budgetId.HasValue)
                query = query.Where(c => c.BudgetId == budgetId);

            ViewBag.Budgets = await _db.Budgets.Where(b => b.EmployeeId == CurrentUserId).ToListAsync();
            return View(await query.OrderByDescending(c => c.SubmittedOn).ToListAsync());
        }

        // ─── NOTIFICATIONS ────────────────────────────────────
        public async Task<IActionResult> Notifications()
        {
            var notifications = await _db.Notifications
                .Where(n => n.UserId == CurrentUserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();
            notifications.Where(n => !n.IsRead).ToList().ForEach(n => n.IsRead = true);
            await _db.SaveChangesAsync();
            return View(notifications);
        }

        [HttpPost]
        public async Task<IActionResult> MarkNotificationRead(int id)
        {
            var n = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == CurrentUserId);
            if (n != null) { n.IsRead = true; await _db.SaveChangesAsync(); }
            return Ok();
        }
    }
}
