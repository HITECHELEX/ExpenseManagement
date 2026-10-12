using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using ClosedXML.Excel;
using ExpenseManagement.Helpers;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.ViewModels;
using ExpenseManagement.Services;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Employee")]
    public class EmployeeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly S3Service _s3;
        private readonly IWebHostEnvironment _env;

        public EmployeeController(AppDbContext db, S3Service s3, IWebHostEnvironment env)
        {
            _db  = db;
            _s3  = s3;
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
                .AsNoTracking()
                .Where(b => b.EmployeeId == userId && b.IsActive)
                .OrderByDescending(b => b.AllocatedOn).ToListAsync();

            // Wallet = only unlocked, active budgets
            var walletBudgets = allBudgets.Where(b => !b.IsLocked).ToList();

            var claims = await _db.ExpenseClaims
                .AsNoTracking()
                .Where(c => c.EmployeeId == userId)
                .OrderByDescending(c => c.ExpenseDate)
                .Take(10).ToListAsync();

            var draftReports = await _db.ExpenseReports
                .AsNoTracking()
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
                .AsNoTracking()
                .Include(b => b.AllocatedBy)
                .Where(b => b.EmployeeId == CurrentUserId)
                .OrderByDescending(b => b.AllocatedOn)
                .ToListAsync();

            ViewBag.TopUpHistory = await _db.WalletTopUps
                .AsNoTracking()
                .Include(w => w.TopUpBy)
                .Where(w => w.EmployeeId == CurrentUserId)
                .OrderByDescending(w => w.TopUpOn)
                .ToListAsync();

            ViewBag.WalletHistory = await _db.WalletTransactions
                .AsNoTracking()
                .Include(t => t.PerformedBy)
                .Where(t => t.EmployeeId == CurrentUserId)
                .OrderByDescending(t => t.Date)
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

            // Enforce 3-draft limit
            var draftCount = await _db.ExpenseReports
                .CountAsync(r => r.EmployeeId == userId && r.Status == ReportStatus.Draft);
            if (draftCount >= 3)
            {
                TempData["Error"] = "You already have 3 in-progress reports. Submit or delete one before creating a new report.";
                return RedirectToAction("MyReports");
            }

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

            // Enforce 3-draft limit (server-side guard)
            var draftCount = await _db.ExpenseReports
                .CountAsync(r => r.EmployeeId == userId && r.Status == ReportStatus.Draft);
            if (draftCount >= 3)
            {
                TempData["Error"] = "Draft limit reached (3/3). Submit or delete an existing draft first.";
                return RedirectToAction("MyReports");
            }

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
                CreatedOn = DateTime.UtcNow
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
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam || r.Status == ReportStatus.RejectedByManagement));

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
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam || r.Status == ReportStatus.RejectedByManagement));

            if (report == null) return NotFound();
            ViewBag.Report = report;

            if (!ModelState.IsValid) return View(model);

            var budget = await _db.Budgets.FindAsync(model.BudgetId);
            if (budget == null || budget.EmployeeId != CurrentUserId)
            {
                ModelState.AddModelError("", "Invalid budget.");
                return View(model);
            }

            // ─── DUPLICATE SUBMIT GUARD ───────────────────────
            // Prevent double-click / duplicate POST creating two identical claims
            var expenseDate = DateTime.SpecifyKind(model.ExpenseDate, DateTimeKind.Utc);
            bool duplicate = await _db.ExpenseClaims.AnyAsync(c =>
                c.ExpenseReportId == model.ReportId &&
                c.EmployeeId      == CurrentUserId  &&
                c.Title           == model.Title    &&
                c.Amount          == model.Amount   &&
                c.ExpenseDate     == expenseDate);
            if (duplicate)
            {
                TempData["Success"] = $"Expense '{model.Title}' already added to this report.";
                return RedirectToAction("ReportDetail", new { id = model.ReportId });
            }

            // ─── UPI REQUIRES RECEIPT ─────────────────────────
            if (model.PaymentMethod == PaymentMethod.UPI &&
                (model.Receipt == null || model.Receipt.Length == 0))
            {
                ModelState.AddModelError("Receipt", "A receipt photo is required for UPI / Online payments.");
                ViewBag.WalletBalance = budget.RemainingBalance;
                return View(model);
            }

            // ─── OVERSPEND ALLOWED ────────────────────────────
            // No balance check — employee can exceed budget.
            // The negative balance will be deducted from their next allocation.

            // Save receipt/bill photos (up to 3 — images compressed automatically)
            string? receiptPath = null; string? receiptFileName = null;
            string? receipt2Path = null; string? receipt2FileName = null;
            string? receipt3Path = null; string? receipt3FileName = null;

            if (model.Receipt != null && model.Receipt.Length > 0)
            {
                var (path, err) = await SaveReceiptAsync(model.Receipt);
                if (err != null) { ModelState.AddModelError("Receipt", err); return View(model); }
                receiptPath = path; receiptFileName = model.Receipt.FileName;
            }
            if (model.Receipt2 != null && model.Receipt2.Length > 0)
            {
                var (path, err) = await SaveReceiptAsync(model.Receipt2);
                if (err != null) { ModelState.AddModelError("Receipt2", err); return View(model); }
                receipt2Path = path; receipt2FileName = model.Receipt2.FileName;
            }
            if (model.Receipt3 != null && model.Receipt3.Length > 0)
            {
                var (path, err) = await SaveReceiptAsync(model.Receipt3);
                if (err != null) { ModelState.AddModelError("Receipt3", err); return View(model); }
                receipt3Path = path; receipt3FileName = model.Receipt3.FileName;
            }

            var claim = new ExpenseClaim
            {
                EmployeeId = CurrentUserId,
                BudgetId = model.BudgetId,
                ExpenseReportId = model.ReportId,
                Title = model.Title,
                Description = model.Description ?? "",
                Category = model.Category,
                Amount = model.Amount,
                ExpenseDate = DateTime.SpecifyKind(model.ExpenseDate, DateTimeKind.Utc),
                ReceiptPath = receiptPath,
                ReceiptFileName = receiptFileName,
                Receipt2Path = receipt2Path,
                Receipt2FileName = receipt2FileName,
                Receipt3Path = receipt3Path,
                Receipt3FileName = receipt3FileName,
                PaymentMethod = model.PaymentMethod,
                Status = ExpenseStatus.Pending,
                SubmittedOn = DateTime.UtcNow
            };

            _db.ExpenseClaims.Add(claim);

            // Deduct from budget — balance CAN go negative (overspend)
            budget.SpentAmount += model.Amount;
            report.TotalClaimed += model.Amount;

            // Log wallet debit transaction
            var otherBudgets = await _db.Budgets
                .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked && b.Id != budget.Id)
                .ToListAsync();
            decimal walletBalanceAfter = otherBudgets.Sum(b => b.RemainingBalance) + budget.RemainingBalance;
            _db.WalletTransactions.Add(new WalletTransaction
            {
                EmployeeId    = CurrentUserId,
                PerformedById = CurrentUserId,
                Type          = WalletTransactionType.Debit,
                Amount        = model.Amount,
                Description   = $"Expense: {model.Title}",
                Date          = DateTime.UtcNow,
                BalanceAfter  = walletBalanceAfter
            });

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
                claim.ExpenseReport?.Status != ReportStatus.RejectedByAccountTeam &&
                claim.ExpenseReport?.Status != ReportStatus.RejectedByManagement)
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
                claim.ExpenseReport?.Status != ReportStatus.RejectedByAccountTeam &&
                claim.ExpenseReport?.Status != ReportStatus.RejectedByManagement)
                return NotFound();

            ViewBag.Claim = claim;
            ViewBag.Report = claim.ExpenseReport;
            var wb = await _db.Budgets
                .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked)
                .ToListAsync();
            ViewBag.WalletBalance = (decimal?)wb.Sum(b => b.RemainingBalance);

            if (!ModelState.IsValid) return View(model);

            // ─── UPI REQUIRES RECEIPT ─────────────────────────
            // Fail only if UPI is selected AND no existing receipt is saved AND no new file uploaded
            if (model.PaymentMethod == PaymentMethod.UPI &&
                string.IsNullOrEmpty(claim.ReceiptPath) &&
                string.IsNullOrEmpty(claim.Receipt2Path) &&
                string.IsNullOrEmpty(claim.Receipt3Path) &&
                (model.Receipt == null || model.Receipt.Length == 0))
            {
                ModelState.AddModelError("Receipt", "A receipt photo is required for UPI / Online payments.");
                return View(model);
            }

            // Handle receipt uploads — keep existing if no new file provided (up to 3 photos)
            if (model.Receipt != null && model.Receipt.Length > 0)
            {
                if (!string.IsNullOrEmpty(claim.ReceiptPath)) await _s3.DeleteAsync(claim.ReceiptPath);
                var (path, err) = await SaveReceiptAsync(model.Receipt);
                if (err != null) { ModelState.AddModelError("Receipt", err); return View(model); }
                claim.ReceiptPath = path; claim.ReceiptFileName = model.Receipt.FileName;
            }
            if (model.Receipt2 != null && model.Receipt2.Length > 0)
            {
                if (!string.IsNullOrEmpty(claim.Receipt2Path)) await _s3.DeleteAsync(claim.Receipt2Path);
                var (path, err) = await SaveReceiptAsync(model.Receipt2);
                if (err != null) { ModelState.AddModelError("Receipt2", err); return View(model); }
                claim.Receipt2Path = path; claim.Receipt2FileName = model.Receipt2.FileName;
            }
            if (model.Receipt3 != null && model.Receipt3.Length > 0)
            {
                if (!string.IsNullOrEmpty(claim.Receipt3Path)) await _s3.DeleteAsync(claim.Receipt3Path);
                var (path, err) = await SaveReceiptAsync(model.Receipt3);
                if (err != null) { ModelState.AddModelError("Receipt3", err); return View(model); }
                claim.Receipt3Path = path; claim.Receipt3FileName = model.Receipt3.FileName;
            }

            // Adjust budget and report totals for the amount change
            decimal diff = model.Amount - claim.Amount;
            if (claim.Budget != null)
                claim.Budget.SpentAmount += diff;
            if (claim.ExpenseReport != null)
                claim.ExpenseReport.TotalClaimed += diff;

            // Log wallet transaction for the amount change
            if (diff != 0)
            {
                var otherBudgetsEdit = await _db.Budgets
                    .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked && b.Id != claim.BudgetId)
                    .ToListAsync();
                decimal walletAfterEdit = otherBudgetsEdit.Sum(b => b.RemainingBalance) + (claim.Budget?.RemainingBalance ?? 0);
                _db.WalletTransactions.Add(new WalletTransaction
                {
                    EmployeeId    = CurrentUserId,
                    PerformedById = CurrentUserId,
                    Type          = diff > 0 ? WalletTransactionType.Debit : WalletTransactionType.Credit,
                    Amount        = Math.Abs(diff),
                    Description   = $"Expense edit: '{claim.Title}' amount {(diff > 0 ? "increased" : "decreased")} by ₹{Math.Abs(diff):N2}",
                    Date          = DateTime.UtcNow,
                    BalanceAfter  = walletAfterEdit
                });
            }

            claim.Title         = model.Title;
            claim.Description   = model.Description ?? "";
            claim.Category      = model.Category;
            claim.Amount        = model.Amount;
            claim.ExpenseDate   = DateTime.SpecifyKind(model.ExpenseDate, DateTimeKind.Utc);
            claim.PaymentMethod = model.PaymentMethod;

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Expense '{claim.Title}' updated successfully.";
            return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
        }

        // ─── FORWARD REPORT TO ACCOUNT TEAM ──────────────────
        [HttpPost]
        public async Task<IActionResult> ForwardReport(int reportId, string? notes, DateTime? fundsReceivedOn)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.EmployeeId == CurrentUserId
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam || r.Status == ReportStatus.RejectedByManagement));

            if (report == null) return NotFound();

            if (!report.ExpenseClaims.Any())
            {
                TempData["Error"] = "Cannot forward an empty report. Please add at least one expense.";
                return RedirectToAction("ReportDetail", new { id = reportId });
            }

            var budget = report.Budget!;
            decimal balanceAtSubmission = budget.RemainingBalance; // can be negative

            // On resubmit: only reset DECLINED claims back to Pending.
            // Previously APPROVED claims stay Approved — AT does not need to re-review them.
            foreach (var claim in report.ExpenseClaims)
            {
                if (claim.Status == ExpenseStatus.Declined)
                {
                    claim.Status = ExpenseStatus.Pending;
                    claim.AccountTeamRemarks = null; // clear old rejection note so AT reviews fresh
                }
                // Approved claims: status stays Approved, remarks kept
            }

            report.RemainingBalanceAtSubmission = balanceAtSubmission;
            report.Status = ReportStatus.SubmittedToAccountTeam;
            report.Summary = notes ?? report.Summary;
            report.ForwardedToAccountTeamOn = DateTime.UtcNow;
            report.TotalClaimed = report.ExpenseClaims.Sum(c => c.Amount);
            report.FundsReceivedOn = fundsReceivedOn.HasValue
                ? DateTime.SpecifyKind(fundsReceivedOn.Value, DateTimeKind.Utc)
                : null;

            // Clear previous review data on resubmission so timeline shows fresh state
            // Management fields
            report.ManagementNotes = null;
            report.ManagementFeedback = null;
            report.ManagementReviewedById = null;
            report.ManagementReviewedOn = null;
            report.DirectorName = null;
            // Account Team fields — cleared so AT reviews fresh on the resubmitted report
            report.AccountTeamNotes = null;
            report.AccountTeamVerifiedById = null;
            report.AccountTeamVerifiedOn = null;
            report.AccountTeamProfileName = null;

            // Notify only the account team member who allocated this budget
            var currentUser = await _db.Users.FindAsync(CurrentUserId);
            string overspendNote = balanceAtSubmission < 0
                ? $" ⚠️ Employee overspent by ₹{Math.Abs(balanceAtSubmission):N2}"
                : $" Returned: ₹{balanceAtSubmission:N2}";
            _db.Notifications.Add(new Notification
            {
                UserId = budget.AllocatedById,
                Message = $"Expense report '{report.ReportTitle}' submitted by {currentUser!.FullName}.{overspendNote}",
                Link = $"/AccountTeam/ReportDetail/{report.Id}",
                Icon = "file-invoice",
                CreatedAt = DateTime.UtcNow
            });

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

            var draftCount = reports.Count(r => r.Status == ReportStatus.Draft);
            ViewBag.DraftCount = draftCount;
            ViewBag.DraftLimit = 3;
            ViewBag.CanCreateReport = draftCount < 3;
            return View(reports);
        }

        // ─── EDIT REPORT (title & summary) ───────────────────
        [HttpGet]
        public async Task<IActionResult> EditReport(int id)
        {
            var report = await _db.ExpenseReports
                .FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == CurrentUserId
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam || r.Status == ReportStatus.RejectedByManagement));

            if (report == null) return NotFound();

            var vm = new CreateReportVM
            {
                BudgetId    = report.BudgetId,
                ReportTitle = report.ReportTitle,
                Summary     = report.Summary
            };

            ViewBag.ReportId = report.Id;
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> EditReport(int id, CreateReportVM model)
        {
            var report = await _db.ExpenseReports
                .FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == CurrentUserId
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam || r.Status == ReportStatus.RejectedByManagement));

            if (report == null) return NotFound();

            ViewBag.ReportId = report.Id;

            if (!ModelState.IsValid) return View(model);

            report.ReportTitle = model.ReportTitle;
            report.Summary     = model.Summary;

            await _db.SaveChangesAsync();

            TempData["Success"] = "Report details updated.";
            return RedirectToAction("ReportDetail", new { id = report.Id });
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

            // For draft or rejected reports, pass the live wallet balance + recent top-ups for modal
            if (report.Status == ReportStatus.Draft || report.Status == ReportStatus.RejectedByAccountTeam || report.Status == ReportStatus.RejectedByManagement)
            {
                var walletBudgets = await _db.Budgets
                    .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked)
                    .ToListAsync();
                ViewBag.WalletBalance = (decimal?)walletBudgets.Sum(b => b.RemainingBalance);

                // Last 3 top-ups so employee can pick a receive date in the Forward modal
                ViewBag.RecentTopUps = await _db.WalletTopUps
                    .Where(w => w.EmployeeId == CurrentUserId)
                    .OrderByDescending(w => w.TopUpOn)
                    .Take(3)
                    .ToListAsync();
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

            if (claim.ExpenseReport?.Status != ReportStatus.Draft && claim.ExpenseReport?.Status != ReportStatus.RejectedByAccountTeam && claim.ExpenseReport?.Status != ReportStatus.RejectedByManagement)
            {
                TempData["Error"] = "Cannot delete expense from a submitted report.";
                return RedirectToAction("ReportDetail", new { id = claim.ExpenseReportId });
            }

            // Restore budget — allow negative to come back up
            if (claim.Budget != null)
                claim.Budget.SpentAmount -= claim.Amount;

            if (claim.ExpenseReport != null)
                claim.ExpenseReport.TotalClaimed = Math.Max(0, claim.ExpenseReport.TotalClaimed - claim.Amount);

            // Log wallet credit for the refund
            var otherBudgetsDel = await _db.Budgets
                .Where(b => b.EmployeeId == CurrentUserId && b.IsActive && !b.IsLocked && b.Id != claim.BudgetId)
                .ToListAsync();
            decimal walletAfterDel = otherBudgetsDel.Sum(b => b.RemainingBalance) + (claim.Budget?.RemainingBalance ?? 0);
            _db.WalletTransactions.Add(new WalletTransaction
            {
                EmployeeId    = CurrentUserId,
                PerformedById = CurrentUserId,
                Type          = WalletTransactionType.Credit,
                Amount        = claim.Amount,
                Description   = $"Expense removed: '{claim.Title}'",
                Date          = DateTime.UtcNow,
                BalanceAfter  = walletAfterDel
            });

            if (!string.IsNullOrEmpty(claim.ReceiptPath))  await _s3.DeleteAsync(claim.ReceiptPath);
            if (!string.IsNullOrEmpty(claim.Receipt2Path)) await _s3.DeleteAsync(claim.Receipt2Path);
            if (!string.IsNullOrEmpty(claim.Receipt3Path)) await _s3.DeleteAsync(claim.Receipt3Path);

            var reportId = claim.ExpenseReportId;
            _db.ExpenseClaims.Remove(claim);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Expense removed from report.";
            return RedirectToAction("ReportDetail", new { id = reportId });
        }

        // ─── DELETE REPORT ────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> DeleteReport(int reportId)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.ExpenseClaims)
                    .ThenInclude(c => c.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.EmployeeId == CurrentUserId
                    && (r.Status == ReportStatus.Draft || r.Status == ReportStatus.RejectedByAccountTeam || r.Status == ReportStatus.RejectedByManagement));

            if (report == null) return NotFound();

            decimal refundTotal = report.ExpenseClaims.Sum(c => c.Amount);
            string reportTitle  = report.ReportTitle;
            int employeeId      = report.EmployeeId;
            int claimCount      = report.ExpenseClaims.Count;

            // Restore budget spent amounts for each expense
            foreach (var claim in report.ExpenseClaims)
            {
                if (claim.Budget != null)
                    claim.Budget.SpentAmount -= claim.Amount;

                // Delete all receipts from S3
                if (!string.IsNullOrEmpty(claim.ReceiptPath))  await _s3.DeleteAsync(claim.ReceiptPath);
                if (!string.IsNullOrEmpty(claim.Receipt2Path)) await _s3.DeleteAsync(claim.Receipt2Path);
                if (!string.IsNullOrEmpty(claim.Receipt3Path)) await _s3.DeleteAsync(claim.Receipt3Path);
            }

            _db.ExpenseClaims.RemoveRange(report.ExpenseClaims);
            _db.ExpenseReports.Remove(report);
            await _db.SaveChangesAsync();

            // Log credit transaction for the refunded amount
            if (refundTotal > 0)
            {
                var activeBudgets = await _db.Budgets.AsNoTracking()
                    .Where(b => b.EmployeeId == employeeId && b.IsActive && !b.IsLocked)
                    .ToListAsync();
                decimal balanceAfterRefund = activeBudgets.Sum(b => b.RemainingBalance);

                _db.WalletTransactions.Add(new WalletTransaction
                {
                    EmployeeId    = employeeId,
                    PerformedById = employeeId,
                    Type          = WalletTransactionType.Credit,
                    Amount        = refundTotal,
                    Description   = $"Refund: Report '{reportTitle}' deleted ({claimCount} expense(s))",
                    Date          = DateTime.UtcNow,
                    BalanceAfter  = balanceAfterRefund
                });
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = "Report and all its expenses have been deleted.";
            return RedirectToAction("MyReports");
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

        // ─── RECEIPT SAVE HELPER (COMPRESS + UPLOAD TO S3) ───
        /// <summary>
        /// Validates, compresses (if image), and uploads the receipt to S3.
        /// Returns (s3Key, null) on success or (null, errorMessage) on failure.
        /// Images: resized to max 1920px, re-encoded as JPEG @ 75% quality, then uploaded.
        /// PDFs: streamed directly to S3 without modification.
        /// The S3 key (e.g. "receipts/abc123.jpg") is what gets stored in the database.
        /// </summary>
        private async Task<(string? path, string? error)> SaveReceiptAsync(IFormFile file)
        {
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(ext))
                return (null, "Only image files (JPG, PNG, GIF, WEBP) and PDFs are allowed.");

            if (file.Length > 10 * 1024 * 1024)
                return (null, "File size must be less than 10MB.");

            if (ext == ".pdf")
            {
                // PDFs: upload directly to S3
                var pdfKey = $"receipts/{Guid.NewGuid()}.pdf";
                using var pdfStream = file.OpenReadStream();
                await _s3.UploadAsync(pdfStream, pdfKey, "application/pdf");
                return (pdfKey, null);
            }

            // Images: compress (max 1920px, JPEG 75%) then upload to S3
            const int MaxDimension = 1920;
            using var inputStream = file.OpenReadStream();
            using var image = await Image.LoadAsync(inputStream);

            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDimension, MaxDimension),
                    Mode = ResizeMode.Max
                }));
            }

            using var compressedMs = new MemoryStream();
            await image.SaveAsync(compressedMs, new JpegEncoder { Quality = 75 });
            compressedMs.Position = 0;

            var imgKey = $"receipts/{Guid.NewGuid()}.jpg";
            await _s3.UploadAsync(compressedMs, imgKey, "image/jpeg");
            return (imgKey, null);
        }

        // ─── DOWNLOAD APPROVED REPORT ZIP (Employee) ─────────
        // Only available once Management has approved the report.
        // Produces the same ZIP as AccountTeam: filled Excel + all receipts.
        public async Task<IActionResult> DownloadReport(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee)
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Include(r => r.AccountTeamVerifiedBy)
                .Include(r => r.ManagementReviewedBy)
                .FirstOrDefaultAsync(r =>
                    r.Id == id &&
                    r.EmployeeId == CurrentUserId &&                // must belong to this employee
                    r.Status == ReportStatus.ApprovedByManagement); // only approved reports

            if (report == null)
            {
                TempData["Error"] = "Report not found or not yet approved.";
                return RedirectToAction("MyReports");
            }

            static string Safe(string s) =>
                string.Concat(s.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_").Trim('_');

            var webRoot      = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var templatePath = Path.Combine(webRoot, "TEMPLATE", "Expense_Report_Template.xlsx");

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // ── Fill Excel template ────────────────────────
                using var xlMs = new MemoryStream();
                using (var wb = new XLWorkbook(templatePath))
                {
                    var ws = wb.Worksheet(1);

                    ws.Cell("C5").Value  = report.ReportTitle;
                    ws.Cell("H5").Value  = $"RPT-{report.Id}";
                    ws.Cell("C6").Value  = report.Employee?.FullName ?? "";
                    ws.Cell("H6").Value  = report.Employee?.Department ?? "";
                    ws.Cell("C7").Value  = report.Budget?.Purpose ?? "";
                    ws.Cell("H7").Value  = "Approved";
                    if (report.RemainingBalanceAtSubmission >= 0)
                    {
                        ws.Cell("A8").Value = "Total Balance";
                        ws.Cell("C8").Value = report.RemainingBalanceAtSubmission;
                    }
                    else
                    {
                        ws.Cell("A8").Value = "Overspent (₹)";
                        ws.Cell("C8").Value = Math.Abs(report.RemainingBalanceAtSubmission);
                    }
                    ws.Cell("H8").Value  = !string.IsNullOrEmpty(report.AccountTeamProfileName)
                        ? report.AccountTeamProfileName
                        : (report.AccountTeamVerifiedBy?.FullName ?? "Account Team");
                    ws.Cell("C9").Value  = report.TotalClaimed;
                    ws.Cell("H9").Value  = !string.IsNullOrEmpty(report.DirectorName)
                        ? report.DirectorName
                        : (report.ManagementReviewedBy?.FullName ?? "Management");
                    ws.Cell("A10").Value = "";
                    // Expenses Receive Date (row 10) — date employee received funds
                    if (report.FundsReceivedOn.HasValue)
                    {
                        ws.Cell("A10").Value = "Expenses Receive Date";
                        ws.Cell("B10").Value = report.FundsReceivedOn.Value.ToIst().ToString("dd/MM/yyyy");
                    }
                    ws.Cell("C10").Value = "";

                    ws.Cell("D13").Value = report.AccountTeamNotes ?? "";
                    ws.Cell("D14").Value = ""; // ManagementNotes is internal — never exported to employee report
                    ws.Cell("D15").Value = report.ManagementFeedback ?? "";
                    ws.Cell("D16").Value = report.Summary ?? "";

                    // ── Expense Date header (col 9) ────────────
                    ws.Cell(19, 9).Value = "Expense Date";
                    ws.Cell(19, 9).Style.Font.Bold = true;
                    ws.Cell(19, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
                    ws.Cell(19, 9).Style.Font.FontColor = XLColor.White;
                    ws.Cell(19, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // ── Expense line items — unlimited rows, bulk insert ───
                    const int FIRST_DATA_ROW    = 20;
                    const int TEMPLATE_DATA_ROWS = 10;   // rows 20-29

                    var sortedClaims = report.ExpenseClaims.OrderBy(x => x.ExpenseDate).ToList();
                    int claimCount   = sortedClaims.Count;

                    // Bulk-insert extra rows ONCE (avoids O(n²) per-row inserts)
                    if (claimCount > TEMPLATE_DATA_ROWS)
                    {
                        int extraRows = claimCount - TEMPLATE_DATA_ROWS;
                        ws.Row(FIRST_DATA_ROW + TEMPLATE_DATA_ROWS - 1).InsertRowsBelow(extraRows);
                        for (int r = FIRST_DATA_ROW + TEMPLATE_DATA_ROWS;
                             r < FIRST_DATA_ROW + claimCount; r++)
                        {
                            for (int col = 1; col <= 9; col++)
                                ws.Cell(r, col).Style = ws.Cell(FIRST_DATA_ROW, col).Style;
                        }
                    }

                    // Write all claim data sequentially
                    for (int i = 0; i < claimCount; i++)
                    {
                        var c   = sortedClaims[i];
                        int row = FIRST_DATA_ROW + i;
                        ws.Cell(row, 1).Value = i + 1;
                        ws.Cell(row, 2).Value = c.Title ?? "";
                        ws.Cell(row, 3).Value = c.Category ?? "";
                        ws.Cell(row, 4).Value = c.PaymentMethod == PaymentMethod.UPI ? "UPI / Online" : "Cash";
                        ws.Cell(row, 5).Value = c.Amount;
                        ws.Cell(row, 5).Style.NumberFormat.Format = "₹#,##0.00";
                        ws.Cell(row, 6).Value = !string.IsNullOrEmpty(c.ReceiptPath) ? "Yes" : "No";
                        ws.Cell(row, 7).Value = c.Description ?? "";
                        ws.Cell(row, 8).Value = c.AccountTeamRemarks ?? "";
                        ws.Cell(row, 9).Value = c.ExpenseDate.ToIst().ToString("dd MMM yyyy");
                    }

                    // Compute actual positions using template-native rows.
                    // Template TOTAL is at row 30 for ≤10 claims; InsertRowsBelow
                    // pushes it to exactly FIRST_DATA_ROW + claimCount for >10 claims.
                    // This prevents duplicate TOTAL/signature rows.
                    int lastDataRow    = FIRST_DATA_ROW + claimCount - 1;
                    int actualTotalRow = FIRST_DATA_ROW + Math.Max(TEMPLATE_DATA_ROWS, claimCount);
                    int actualSigRow   = actualTotalRow + 4;  // template sig-names row (A34 for ≤10)

                    // Clear empty data rows between last claim and TOTAL
                    // (for ≤10 claims: rows 21-29 would otherwise show blank styled cells)
                    for (int r = lastDataRow + 1; r < actualTotalRow; r++)
                        for (int c = 1; c <= 9; c++)
                            ws.Cell(r, c).Value = "";

                    // Update TOTAL formula only — template has the dark-blue styling already
                    ws.Cell(actualTotalRow, 5).FormulaA1 = $"=SUM(E{FIRST_DATA_ROW}:E{lastDataRow})";
                    ws.Cell(actualTotalRow, 5).Style.NumberFormat.Format = "₹#,##0.00";

                    // Resolve reviewer / director names
                    var atName  = !string.IsNullOrEmpty(report.AccountTeamProfileName)
                        ? report.AccountTeamProfileName
                        : (report.AccountTeamVerifiedBy?.FullName ?? "Account Team");
                    var dirName = !string.IsNullOrEmpty(report.DirectorName)
                        ? report.DirectorName
                        : (report.ManagementReviewedBy?.FullName ?? "Management");
                    var empName = report.Employee?.FullName ?? "";

                    // Write names into the existing template signature row
                    ws.Cell(actualSigRow, 1).Value = empName;
                    ws.Cell(actualSigRow, 4).Value = atName;
                    ws.Cell(actualSigRow, 7).Value = dirName;

                    wb.SaveAs(xlMs);
                }

                xlMs.Seek(0, SeekOrigin.Begin);
                var xlEntry = archive.CreateEntry($"Report_{report.Id}_{Safe(report.ReportTitle)}.xlsx");
                using (var xlOut = xlEntry.Open())
                    await xlMs.CopyToAsync(xlOut);

                // ── Attach receipts from S3 ────────────────────
                foreach (var c in report.ExpenseClaims.Where(x => !string.IsNullOrEmpty(x.ReceiptPath)))
                {
                    try
                    {
                        var (s3Stream, _, ext) = await _s3.DownloadAsync(c.ReceiptPath!);
                        var billEntry = archive.CreateEntry($"Bills/{Safe(c.Title)}_{(int)c.Amount}_{c.ExpenseDate:yyyyMMdd}{ext}");
                        using var bs = billEntry.Open();
                        await s3Stream.CopyToAsync(bs);
                        await s3Stream.DisposeAsync();
                    }
                    catch
                    {
                        // Skip missing receipts — don't fail the whole ZIP
                    }
                }
            }

            ms.Seek(0, SeekOrigin.Begin);
            return File(
                ms.ToArray(),
                "application/zip",
                $"ApprovedReport_{Safe(report.ReportTitle)}_{DateTime.UtcNow:yyyyMMdd}.zip"
            );
        }
    }
}
