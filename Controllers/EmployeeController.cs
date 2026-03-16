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

            // All active budgets (including locked for display)
            var allBudgets = await _db.Budgets
                .Where(b => b.EmployeeId == userId && b.IsActive)
                .OrderByDescending(b => b.AllocatedOn).ToListAsync();

            // Wallet = only unlocked, active budgets
            var walletBudgets = allBudgets.Where(b => !b.IsLocked).ToList();

            var claims = await _db.ExpenseClaims
                .Where(c => c.EmployeeId == userId)
                .OrderByDescending(c => c.ExpenseDate)
                .Take(10).ToListAsync();

            var draftReports = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Where(r => r.EmployeeId == userId && r.Status == ReportStatus.Draft)
                .ToListAsync();

            var unread = await _db.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);

            var vm = new EmployeeDashboardVM
            {
                Employee     = employee!,
                Budgets      = allBudgets,
                RecentClaims = claims,
                DraftReports = draftReports,
                // Totals based on wallet (active + unlocked) only
                TotalAllocated = walletBudgets.Sum(b => b.TotalAmount),
                TotalSpent     = walletBudgets.Sum(b => b.SpentAmount),
                TotalBalance   = walletBudgets.Sum(b => b.RemainingBalance),
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

            ViewBag.TopUpHistory = await _db.WalletTopUps
                .Include(w => w.TopUpBy)
                .Where(w => w.EmployeeId == CurrentUserId)
                .OrderByDescending(w => w.TopUpOn)
                .ToListAsync();

            return View(budgets);
        }

        // ─── CREATE DRAFT REPORT (wallet-based) ──────────────
        // Report is created against the employee's global wallet balance
        // (sum of all active unlocked budgets). No per-report budget selection.
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

            TempData["Success"] = "Draft report created. Add daily expenses then forward to Account Team when done.";
            return RedirectToAction("ReportDetail", new { id = report.Id });
        }

        // ─── ADD EXPENSE TO DRAFT (ALLOWS OVERSPEND) ─────────
        [HttpGet]
        public async Task<IActionResult> AddExpense(int reportId)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.EmployeeId == CurrentUserId
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam));

            if (report == null) return NotFound();

            ViewBag.Report = report;
            // Pass global wallet balance (not just per-budget)
            var wb = await _db.Budgets
                .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked)
                .ToListAsync();
            ViewBag.WalletBalance = (decimal?)wb.Sum(b => b.RemainingBalance);

            var vm = new ExpenseClaimVM
            {
                BudgetId = report.BudgetId,
                ReportId = reportId
            };
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> AddExpense(ExpenseClaimVM model)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == model.ReportId && r.EmployeeId == CurrentUserId
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam));

            if (report == null) return NotFound();
            ViewBag.Report = report;

            if (!ModelState.IsValid) return View(model);

            var budget = await _db.Budgets.FindAsync(model.BudgetId);
            if (budget == null || budget.EmployeeId != CurrentUserId)
            {
                ModelState.AddModelError("", "Invalid budget.");
                return View(model);
            }

            // ─── OVERSPEND ALLOWED ────────────────────────────
            // No balance check — employee can exceed budget.
            // The negative balance will be deducted from their next allocation.

            // Save receipt/bill photo
            string? receiptPath = null;
            string? receiptFileName = null;
            if (model.Receipt != null && model.Receipt.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".webp" };
                var ext = Path.GetExtension(model.Receipt.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(ext))
                {
                    ModelState.AddModelError("Receipt", "Only image files (JPG, PNG, GIF, WEBP) and PDFs are allowed.");
                    return View(model);
                }
                if (model.Receipt.Length > 10 * 1024 * 1024)
                {
                    ModelState.AddModelError("Receipt", "File size must be less than 10MB.");
                    return View(model);
                }
                var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var uploads = Path.Combine(webRoot, "uploads", "receipts");
                Directory.CreateDirectory(uploads);
                var fileName = $"{Guid.NewGuid()}{ext}";
                using var stream = new FileStream(Path.Combine(uploads, fileName), FileMode.Create);
                await model.Receipt.CopyToAsync(stream);
                receiptPath = $"/uploads/receipts/{fileName}";
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
                PaymentMethod = model.PaymentMethod,
                Status = ExpenseStatus.Pending,
                SubmittedOn = DateTime.Now
            };

            _db.ExpenseClaims.Add(claim);

            // Deduct from budget — balance CAN go negative (overspend)
            budget.SpentAmount += model.Amount;
            report.TotalClaimed += model.Amount;

            await _db.SaveChangesAsync();

            // Inform employee of their new balance (may be negative)
            decimal newBalance = budget.RemainingBalance;
            string balanceNote;
            if (newBalance >= 0)
                balanceNote = $"Remaining budget: ₹{newBalance:N2}";
            else
                balanceNote = $"⚠️ Over budget by ₹{Math.Abs(newBalance):N2} — this will be recovered from your next allocation.";

            TempData["Success"] = $"Expense of ₹{model.Amount:N2} added. {balanceNote}";
            return RedirectToAction("ReportDetail", new { id = model.ReportId });
        }

        // ─── EDIT EXPENSE IN DRAFT ────────────────────────────
        [HttpGet]
        public async Task<IActionResult> EditExpense(int id)
        {
            var claim = await _db.ExpenseClaims
                .Include(c => c.Budget)
                .Include(c => c.ExpenseReport)
                .FirstOrDefaultAsync(c => c.Id == id && c.EmployeeId == CurrentUserId);

            if (claim == null) return NotFound();

            if (claim.ExpenseReport?.Status != ReportStatus.Draft &&
                claim.ExpenseReport?.Status != ReportStatus.RejectedByAccountTeam)
            {
                TempData["Error"] = "Cannot edit an expense from a submitted report.";
                return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
            }

            ViewBag.Claim = claim;
            ViewBag.Report = claim.ExpenseReport;
            var wb = await _db.Budgets
                .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked)
                .ToListAsync();
            ViewBag.WalletBalance = (decimal?)wb.Sum(b => b.RemainingBalance);

            var vm = new ExpenseClaimVM
            {
                BudgetId     = claim.BudgetId,
                ReportId     = claim.ExpenseReportId,
                Title        = claim.Title,
                Description  = claim.Description,
                Category     = claim.Category,
                Amount       = claim.Amount,
                ExpenseDate  = claim.ExpenseDate,
                PaymentMethod = claim.PaymentMethod
            };
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> EditExpense(int id, ExpenseClaimVM model)
        {
            var claim = await _db.ExpenseClaims
                .Include(c => c.Budget)
                .Include(c => c.ExpenseReport)
                .FirstOrDefaultAsync(c => c.Id == id && c.EmployeeId == CurrentUserId);

            if (claim == null) return NotFound();

            if (claim.ExpenseReport?.Status != ReportStatus.Draft &&
                claim.ExpenseReport?.Status != ReportStatus.RejectedByAccountTeam)
                return NotFound();

            ViewBag.Claim = claim;
            ViewBag.Report = claim.ExpenseReport;
            var wb = await _db.Budgets
                .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked)
                .ToListAsync();
            ViewBag.WalletBalance = (decimal?)wb.Sum(b => b.RemainingBalance);

            if (!ModelState.IsValid) return View(model);

            // Handle receipt upload — keep existing if no new file provided
            if (model.Receipt != null && model.Receipt.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".webp" };
                var ext = Path.GetExtension(model.Receipt.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(ext))
                {
                    ModelState.AddModelError("Receipt", "Only image files (JPG, PNG, GIF, WEBP) and PDFs are allowed.");
                    return View(model);
                }
                if (model.Receipt.Length > 10 * 1024 * 1024)
                {
                    ModelState.AddModelError("Receipt", "File size must be less than 10MB.");
                    return View(model);
                }

                // Delete old receipt file if it exists
                if (!string.IsNullOrEmpty(claim.ReceiptPath))
                {
                    var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                    var oldFile = Path.Combine(webRoot, claim.ReceiptPath.TrimStart('/'));
                    if (System.IO.File.Exists(oldFile)) System.IO.File.Delete(oldFile);
                }

                var wr = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var uploads = Path.Combine(wr, "uploads", "receipts");
                Directory.CreateDirectory(uploads);
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(model.Receipt.FileName).ToLowerInvariant()}";
                using var stream = new FileStream(Path.Combine(uploads, fileName), FileMode.Create);
                await model.Receipt.CopyToAsync(stream);
                claim.ReceiptPath     = $"/uploads/receipts/{fileName}";
                claim.ReceiptFileName = model.Receipt.FileName;
            }

            // Adjust budget and report totals for the amount change
            decimal diff = model.Amount - claim.Amount;
            if (claim.Budget != null)
                claim.Budget.SpentAmount += diff;
            if (claim.ExpenseReport != null)
                claim.ExpenseReport.TotalClaimed += diff;

            claim.Title         = model.Title;
            claim.Description   = model.Description;
            claim.Category      = model.Category;
            claim.Amount        = model.Amount;
            claim.ExpenseDate   = model.ExpenseDate;
            claim.PaymentMethod = model.PaymentMethod;

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Expense '{claim.Title}' updated successfully.";
            return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
        }

        // ─── FORWARD REPORT TO ACCOUNT TEAM ──────────────────
        [HttpPost]
        public async Task<IActionResult> ForwardReport(int reportId, string? notes)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.EmployeeId == CurrentUserId
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam));

            if (report == null) return NotFound();

            if (!report.ExpenseClaims.Any())
            {
                TempData["Error"] = "Cannot forward an empty report. Please add at least one expense.";
                return RedirectToAction("ReportDetail", new { id = reportId });
            }

            var budget = report.Budget!;
            decimal balanceAtSubmission = budget.RemainingBalance; // can be negative

            // Reset per-expense review statuses so account team reviews fresh on resubmit
            foreach (var claim in report.ExpenseClaims)
            {
                claim.Status = ExpenseStatus.Pending;
                claim.AccountTeamRemarks = null;
            }

            report.RemainingBalanceAtSubmission = balanceAtSubmission;
            report.Status = ReportStatus.SubmittedToAccountTeam;
            report.Summary = notes ?? report.Summary;
            report.ForwardedToAccountTeamOn = DateTime.Now;
            report.TotalClaimed = report.ExpenseClaims.Sum(c => c.Amount);

            // Notify account team
            var currentUser = await _db.Users.FindAsync(CurrentUserId);
            var accountTeamMembers = await _db.Users.Where(u => u.Role == UserRole.AccountTeam).ToListAsync();
            string overspendNote = balanceAtSubmission < 0
                ? $" ⚠️ Employee overspent by ₹{Math.Abs(balanceAtSubmission):N2}"
                : $" Returned: ₹{balanceAtSubmission:N2}";
            foreach (var at in accountTeamMembers)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = at.Id,
                    Message = $"Expense report '{report.ReportTitle}' submitted by {currentUser!.FullName}.{overspendNote}",
                    Link = $"/AccountTeam/ReportDetail/{report.Id}",
                    Icon = "file-invoice",
                    CreatedAt = DateTime.Now
                });
            }

            await _db.SaveChangesAsync();

            string successMsg = balanceAtSubmission >= 0
                ? $"Report forwarded. Remaining balance of ₹{balanceAtSubmission:N2} will be returned to your budget."
                : $"Report forwarded. Note: you overspent by ₹{Math.Abs(balanceAtSubmission):N2}. This will be auto-deducted from your next budget allocation.";

            TempData["Success"] = successMsg;
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

            // For draft or rejected-by-AT reports, pass the live wallet balance
            if (report.Status == ReportStatus.Draft || report.Status == ReportStatus.RejectedByAccountTeam)
            {
                var walletBudgets = await _db.Budgets
                    .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked)
                    .ToListAsync();
                ViewBag.WalletBalance = (decimal?)walletBudgets.Sum(b => b.RemainingBalance);
            }

            return View(report);
        }

        // ─── DELETE EXPENSE FROM DRAFT ────────────────────────
        [HttpPost]
        public async Task<IActionResult> DeleteExpense(int claimId)
        {
            var claim = await _db.ExpenseClaims
                .Include(c => c.Budget)
                .Include(c => c.ExpenseReport)
                .FirstOrDefaultAsync(c => c.Id == claimId && c.EmployeeId == CurrentUserId);

            if (claim == null) return NotFound();

            if (claim.ExpenseReport?.Status != ReportStatus.Draft && claim.ExpenseReport?.Status != ReportStatus.RejectedByAccountTeam)
            {
                TempData["Error"] = "Cannot delete expense from a submitted report.";
                return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
            }

            // Restore budget — allow negative to come back up
            if (claim.Budget != null)
                claim.Budget.SpentAmount -= claim.Amount;

            if (claim.ExpenseReport != null)
                claim.ExpenseReport.TotalClaimed = Math.Max(0, claim.ExpenseReport.TotalClaimed - claim.Amount);

            if (!string.IsNullOrEmpty(claim.ReceiptPath))
            {
                var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                var filePath = Path.Combine(webRoot, claim.ReceiptPath.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            var reportId = claim.ExpenseReportId;
            _db.ExpenseClaims.Remove(claim);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Expense removed from report.";
            return RedirectToAction("ReportDetail", new { id = reportId });
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

            ViewBag.Budgets = await _db.Budgets
                .Where(b => b.EmployeeId == CurrentUserId).ToListAsync();

            return View(await query.OrderByDescending(c => c.SubmittedOn).ToListAsync());
        }

        // ─── NOTIFICATIONS ────────────────────────────────────
        public async Task<IActionResult> Notifications()
        {
            var notifications = await _db.Notifications
                .Where(n => n.UserId == CurrentUserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            var unread = notifications.Where(n => !n.IsRead).ToList();
            unread.ForEach(n => n.IsRead = true);
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

        // ─── EXPENSE GUIDELINE ────────────────────────────────
        public IActionResult ExpenseGuideline() => View();
    }
}
