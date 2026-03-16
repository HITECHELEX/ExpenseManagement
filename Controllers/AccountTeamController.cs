using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.ViewModels;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "AccountTeam")]
    public class AccountTeamController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AccountTeamController(AppDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ─── DASHBOARD ────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            var pendingReports = await _db.ExpenseReports
                .Include(r => r.Employee)
                .Include(r => r.ExpenseClaims)
                .Where(r => r.Status == ReportStatus.SubmittedToAccountTeam)
                .Take(5).ToListAsync();

            var recentTopUps = await _db.WalletTopUps
                .Include(w => w.Employee)
                .Include(w => w.TopUpBy)
                .OrderByDescending(w => w.TopUpOn)
                .Take(10).ToListAsync();

            var unread = await _db.Notifications
                .CountAsync(n => n.UserId == CurrentUserId && !n.IsRead);

            var vm = new AccountDashboardVM
            {
                PendingClaims     = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Pending),
                ApprovedClaims    = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Approved),
                DeclinedClaims    = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Declined),
                PendingReports    = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.SubmittedToAccountTeam),
                TotalEmployees    = await _db.Users.CountAsync(u => u.Role == UserRole.Employee),
                TotalAllocated    = await _db.Budgets.SumAsync(b => b.TotalAmount),
                TotalApproved     = await _db.ExpenseClaims.Where(c => c.Status == ExpenseStatus.Approved).SumAsync(c => c.Amount),
                TotalTopUps       = await _db.WalletTopUps.CountAsync(),
                TotalTopUpAmount  = await _db.WalletTopUps.SumAsync(w => w.Amount),
                PendingReports_List = pendingReports,
                RecentTopUps      = recentTopUps,
                UnreadNotifications = unread
            };

            return View(vm);
        }

        // ─── TOP UP WALLET ────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AllocateBudget()
        {
            var employees = await _db.Users.Where(u => u.Role == UserRole.Employee)
                .Include(u => u.Budgets).ToListAsync();
            ViewBag.Employees = employees;

            var walletMap = new Dictionary<int, decimal>();
            foreach (var emp in employees)
            {
                var active = emp.Budgets.Where(b => b.IsActive && !b.IsLocked).ToList();
                walletMap[emp.Id] = active.Sum(b => b.RemainingBalance);
            }
            ViewBag.WalletMap = walletMap;

            return View(new AllocateBudgetVM());
        }

        [HttpPost]
        public async Task<IActionResult> AllocateBudget(AllocateBudgetVM model)
        {
            var employees = await _db.Users.Where(u => u.Role == UserRole.Employee)
                .Include(u => u.Budgets).ToListAsync();
            ViewBag.Employees = employees;
            var walletMap = new Dictionary<int, decimal>();
            foreach (var emp in employees)
            {
                var active = emp.Budgets.Where(b => b.IsActive && !b.IsLocked).ToList();
                walletMap[emp.Id] = active.Sum(b => b.RemainingBalance);
            }
            ViewBag.WalletMap = walletMap;

            if (!ModelState.IsValid) return View(model);

            var employee = await _db.Users.FindAsync(model.EmployeeId);
            if (employee == null || employee.Role != UserRole.Employee)
            {
                ModelState.AddModelError("", "Invalid employee selected.");
                return View(model);
            }

            // Find the single active wallet for this employee
            var wallet = await _db.Budgets
                .Where(b => b.EmployeeId == model.EmployeeId && b.IsActive && !b.IsLocked)
                .OrderByDescending(b => b.AllocatedOn)
                .FirstOrDefaultAsync();

            decimal newBalance;
            if (wallet != null)
            {
                // Top up: just add to TotalAmount — RemainingBalance corrects automatically
                wallet.TotalAmount    += model.TotalAmount;
                wallet.Purpose         = model.Purpose;
                wallet.AllocatedById   = CurrentUserId;
                wallet.AllocatedOn     = DateTime.Now;
                wallet.OverspentAmount = 0; // clear; overspend is visible via negative RemainingBalance
                if (model.ValidUntil.HasValue)
                    wallet.ValidUntil = model.ValidUntil;
                newBalance = wallet.RemainingBalance;
            }
            else
            {
                // First top-up ever — create the wallet
                wallet = new Budget
                {
                    EmployeeId     = model.EmployeeId,
                    AllocatedById  = CurrentUserId,
                    TotalAmount    = model.TotalAmount,
                    SpentAmount    = 0,
                    ReturnedBalance  = 0,
                    OverspentAmount  = 0,
                    Purpose        = model.Purpose,
                    ValidUntil     = model.ValidUntil,
                    AllocatedOn    = DateTime.Now,
                    IsActive       = true,
                    IsLocked       = false
                };
                _db.Budgets.Add(wallet);
                newBalance = model.TotalAmount;
            }

            _db.Notifications.Add(new Notification
            {
                UserId    = employee.Id,
                Message   = $"Your wallet was topped up by ₹{model.TotalAmount:N2} ({model.Purpose}). Available balance: ₹{newBalance:N2}.",
                Link      = "/Employee/MyBudgets",
                Icon      = "wallet",
                CreatedAt = DateTime.Now
            });

            _db.WalletTopUps.Add(new WalletTopUp
            {
                EmployeeId   = model.EmployeeId,
                TopUpById    = CurrentUserId,
                Amount       = model.TotalAmount,
                Purpose      = model.Purpose,
                TopUpOn      = DateTime.Now,
                BalanceAfter = newBalance
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = $"₹{model.TotalAmount:N2} added to {employee.FullName}'s wallet. New balance: ₹{newBalance:N2}.";
            return RedirectToAction("Budgets");
        }

        // ─── WALLET TOP-UP HISTORY (JSON for inline load) ────
        [HttpGet]
        public async Task<IActionResult> GetTopUpHistory(int employeeId)
        {
            var history = await _db.WalletTopUps
                .Include(w => w.TopUpBy)
                .Where(w => w.EmployeeId == employeeId)
                .OrderByDescending(w => w.TopUpOn)
                .Select(w => new
                {
                    w.Id,
                    w.Amount,
                    w.Purpose,
                    TopUpOn     = w.TopUpOn.ToString("dd MMM yyyy, hh:mm tt"),
                    TopUpBy     = w.TopUpBy != null ? w.TopUpBy.FullName : "Account Team",
                    w.BalanceAfter
                })
                .ToListAsync();
            return Json(history);
        }

        private async Task<decimal> GetTotalOwedByEmployee(int employeeId)
        {
            var budgets = await _db.Budgets.Where(b => b.EmployeeId == employeeId).ToListAsync();
            return budgets.Where(b => b.OverspentAmount > 0).Sum(b => b.OverspentAmount)
                 + budgets.Where(b => b.IsActive && b.RemainingBalance < 0).Sum(b => Math.Abs(b.RemainingBalance));
        }

        // ─── VIEW ALL BUDGETS ─────────────────────────────────
        public async Task<IActionResult> Budgets()
        {
            var budgets = await _db.Budgets
                .Include(b => b.Employee).Include(b => b.AllocatedBy)
                .OrderByDescending(b => b.AllocatedOn).ToListAsync();
            return View(budgets);
        }

        // ─── EMPLOYEE WALLET HISTORY ──────────────────────────
        public async Task<IActionResult> EmployeeWalletHistory(int id)
        {
            var employee = await _db.Users
                .Include(u => u.Budgets)
                .FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee);

            if (employee == null) return NotFound();

            var activeBudgets = employee.Budgets.Where(b => b.IsActive && !b.IsLocked).ToList();

            var topUps = await _db.WalletTopUps
                .Include(w => w.TopUpBy)
                .Where(w => w.EmployeeId == id)
                .OrderByDescending(w => w.TopUpOn)
                .ToListAsync();

            var vm = new EmployeeWalletHistoryVM
            {
                Employee         = employee,
                WalletBalance    = activeBudgets.Sum(b => b.RemainingBalance),
                TotalAllocated   = activeBudgets.Sum(b => b.TotalAmount),
                TotalSpent       = activeBudgets.Sum(b => b.SpentAmount),
                TotalTopUps      = topUps.Count,
                TotalTopUpAmount = topUps.Sum(w => w.Amount),
                TopUps           = topUps,
                Budgets          = employee.Budgets.OrderByDescending(b => b.AllocatedOn).ToList()
            };

            return View(vm);
        }

        // ─── EMPLOYEES ────────────────────────────────────────
        public async Task<IActionResult> Employees()
        {
            var employees = await _db.Users
                .Where(u => u.Role == UserRole.Employee)
                .Include(u => u.Budgets)
                .ToListAsync();
            return View(employees);
        }

        [HttpGet]
        public IActionResult CreateEmployee() => View(new RegisterVM { Role = UserRole.Employee });

        [HttpPost]
        public async Task<IActionResult> CreateEmployee(RegisterVM model)
        {
            if (!ModelState.IsValid) return View(model);
            if (await _db.Users.AnyAsync(u => u.Email == model.Email))
            {
                ModelState.AddModelError("Email", "Email already exists.");
                return View(model);
            }
            var user = new User
            {
                FullName = model.FullName,
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
                Role = UserRole.Employee,
                Department = model.Department,
                CreatedAt = DateTime.Now
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Employee '{model.FullName}' created successfully.";
            return RedirectToAction("Employees");
        }

        [HttpGet]
        public async Task<IActionResult> EditEmployee(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null || user.Role != UserRole.Employee) return NotFound();
            var vm = new EditEmployeeVM
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Department = user.Department
            };
            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> EditEmployee(EditEmployeeVM model)
        {
            if (!ModelState.IsValid) return View(model);
            var user = await _db.Users.FindAsync(model.Id);
            if (user == null || user.Role != UserRole.Employee) return NotFound();
            if (await _db.Users.AnyAsync(u => u.Email == model.Email && u.Id != model.Id))
            {
                ModelState.AddModelError("Email", "Email is already in use by another account.");
                return View(model);
            }
            user.FullName = model.FullName;
            user.Email = model.Email;
            user.Department = model.Department;
            if (!string.IsNullOrWhiteSpace(model.NewPassword))
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Employee '{user.FullName}' updated successfully.";
            return RedirectToAction("Employees");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            var user = await _db.Users
                .Include(u => u.Budgets)
                .Include(u => u.ExpenseClaims)
                .Include(u => u.ExpenseReports)
                .FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee);
            if (user == null) return NotFound();
            if (user.Budgets.Any() || user.ExpenseClaims.Any() || user.ExpenseReports.Any())
            {
                TempData["Error"] = $"Cannot delete '{user.FullName}' because they have existing budgets, claims, or reports.";
                return RedirectToAction("Employees");
            }
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Employee '{user.FullName}' deleted successfully.";
            return RedirectToAction("Employees");
        }

        // ─── ALLOCATION BREAKDOWN ─────────────────────────────
        public async Task<IActionResult> AllocationBreakdown()
        {
            var budgets = await _db.Budgets
                .Include(b => b.Employee)
                .OrderBy(b => b.Employee.FullName)
                .ToListAsync();

            var topUps = await _db.WalletTopUps
                .OrderByDescending(w => w.TopUpOn)
                .ToListAsync();

            var reports = await _db.ExpenseReports.ToListAsync();

            var byEmployee = budgets
                .GroupBy(b => b.EmployeeId)
                .Select(g => new ExpenseManagement.ViewModels.EmployeeAllocationRow
                {
                    EmployeeId     = g.Key,
                    EmployeeName   = g.First().Employee?.FullName ?? "",
                    Department     = g.First().Employee?.Department ?? "",
                    TotalAllocated = g.Sum(b => b.TotalAmount),
                    TotalSpent     = g.Sum(b => b.SpentAmount),
                    Remaining      = g.Sum(b => b.RemainingBalance),
                    BudgetCount    = g.Count(),
                    ReportCount    = reports.Count(r => r.EmployeeId == g.Key)
                })
                .OrderByDescending(e => e.TotalAllocated)
                .ToList();

            var byMonth = topUps
                .GroupBy(t => new { t.TopUpOn.Year, t.TopUpOn.Month })
                .Select(g => new ExpenseManagement.ViewModels.MonthlyAllocationRow
                {
                    Year       = g.Key.Year,
                    Month      = g.Key.Month,
                    MonthLabel = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    TotalAdded = g.Sum(t => t.Amount),
                    TopUpCount = g.Count()
                })
                .OrderByDescending(m => m.Year).ThenByDescending(m => m.Month)
                .ToList();

            var vm = new ExpenseManagement.ViewModels.AllocationBreakdownVM
            {
                TotalAllocated = budgets.Sum(b => b.TotalAmount),
                TotalSpent     = budgets.Sum(b => b.SpentAmount),
                TotalRemaining = budgets.Sum(b => b.RemainingBalance),
                ByEmployee     = byEmployee,
                ByMonth        = byMonth,
                AllBudgets     = budgets.OrderByDescending(b => b.AllocatedOn).ToList()
            };

            return View(vm);
        }

        // ─── REVIEW REPORTS ───────────────────────────────────
        public async Task<IActionResult> Reports(string? status)
        {
            var query = _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.Budget).AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var s))
                query = query.Where(r => r.Status == s);
            else
                query = query.Where(r => r.Status == ReportStatus.SubmittedToAccountTeam);

            ViewBag.CurrentStatus = status ?? "SubmittedToAccountTeam";
            return View(await query.OrderByDescending(r => r.CreatedOn).ToListAsync());
        }

        public async Task<IActionResult> ReportDetail(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Include(r => r.AccountTeamVerifiedBy)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null) return NotFound();
            return View(report);
        }

        [HttpPost]
        public async Task<IActionResult> ReviewExpense(int reportId, int claimId, string reviewAction, string? feedback)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.ExpenseClaims)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.SubmittedToAccountTeam);

            if (report == null) return NotFound();

            var claim = report.ExpenseClaims.FirstOrDefault(c => c.Id == claimId);
            if (claim == null) return NotFound();

            claim.ReviewedById = CurrentUserId;
            claim.ReviewedOn = DateTime.Now;

            if (reviewAction == "approve")
            {
                claim.Status = ExpenseStatus.Approved;
                claim.AccountTeamRemarks = null;
            }
            else if (reviewAction == "decline")
            {
                claim.Status = ExpenseStatus.Declined;
                claim.AccountTeamRemarks = string.IsNullOrWhiteSpace(feedback) ? "Rejected by Account Team" : feedback;
            }
            else
            {
                claim.Status = ExpenseStatus.Pending;
                claim.AccountTeamRemarks = null;
            }

            await _db.SaveChangesAsync();
            return RedirectToAction("ReportDetail", new { id = reportId });
        }

        [HttpPost]
        public async Task<IActionResult> ForwardToManagement(int reportId, string? notes)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee)
                .Include(r => r.ExpenseClaims)
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.SubmittedToAccountTeam);

            if (report == null) return NotFound();

            if (report.ExpenseClaims.Any(c => c.Status != ExpenseStatus.Approved))
            {
                TempData["Error"] = "Please review and approve all expenses before forwarding to Management.";
                return RedirectToAction("ReportDetail", new { id = reportId });
            }

            report.Status = ReportStatus.ForwardedToManagement;
            report.AccountTeamNotes = notes;
            report.AccountTeamVerifiedById = CurrentUserId;
            report.AccountTeamVerifiedOn = DateTime.Now;
            report.TotalApproved = report.ExpenseClaims.Sum(c => c.Amount);

            string overspendNote = report.RemainingBalanceAtSubmission < 0
                ? $" ⚠️ Overspent by ₹{Math.Abs(report.RemainingBalanceAtSubmission):N2}"
                : "";

            var mgmt = await _db.Users.Where(u => u.Role == UserRole.Management).ToListAsync();
            foreach (var m in mgmt)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = m.Id,
                    Message = $"Expense report '{report.ReportTitle}' from {report.Employee!.FullName} is ready for review.{overspendNote}",
                    Link = $"/Management/ReportDetail/{report.Id}",
                    Icon = "tasks",
                    CreatedAt = DateTime.Now
                });
            }

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' was approved by Account Team and forwarded to Management.",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = "check-circle",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = "Report approved and forwarded to Management.";
            return RedirectToAction("Reports");
        }

        [HttpPost]
        public async Task<IActionResult> RejectReport(int reportId, string? rejectionReason)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee)
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.SubmittedToAccountTeam);

            if (report == null) return NotFound();

            // Reverse overspend that was recorded at submission so resubmit calculates correctly
            if (report.RemainingBalanceAtSubmission < 0 && report.Budget != null)
            {
                decimal overspent = Math.Abs(report.RemainingBalanceAtSubmission);
                report.Budget.OverspentAmount = Math.Max(0, report.Budget.OverspentAmount - overspent);
            }

            report.Status = ReportStatus.RejectedByAccountTeam;
            report.AccountTeamNotes = rejectionReason;
            report.AccountTeamVerifiedById = CurrentUserId;
            report.AccountTeamVerifiedOn = DateTime.Now;
            report.RemainingBalanceAtSubmission = 0;

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' was rejected by Account Team. Reason: {rejectionReason}. Please review and resubmit.",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = "times-circle",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();
            TempData["Error"] = $"Report '{report.ReportTitle}' rejected. Employee has been notified.";
            return RedirectToAction("Reports");
        }

        // ─── DOWNLOAD REPORT ZIP ──────────────────────────────
        public async Task<IActionResult> DownloadReport(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee)
                .Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Include(r => r.AccountTeamVerifiedBy)
                .Include(r => r.ManagementReviewedBy)
                .FirstOrDefaultAsync(r => r.Id == id && r.Status == ReportStatus.ApprovedByManagement);

            if (report == null)
            {
                TempData["Error"] = "Report not found or not yet approved.";
                return RedirectToAction("Reports");
            }

            // Strip invalid filename chars
            static string Safe(string s) =>
                string.Concat(s.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_").Trim('_');

            // ── Build filled Excel from template ──────────────
            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            var templatePath = Path.Combine(webRoot, "TEMPLATE", "Expense_Report_Template.xlsx");

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // Fill template workbook
                using var xlMs = new MemoryStream();
                using (var wb = new XLWorkbook(templatePath))
                {
                    var ws = wb.Worksheet(1);

                    // ── Header fields ──────────────────────────
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
                    ws.Cell("H8").Value  = report.AccountTeamVerifiedBy?.FullName ?? "";
                    ws.Cell("C9").Value  = report.TotalClaimed;
                    ws.Cell("H9").Value  = report.ManagementReviewedBy?.FullName ?? "";
                    ws.Cell("A10").Value = "";
                    ws.Cell("C10").Value = "";

                    // ── Review & approval notes ────────────────
                    ws.Cell("D13").Value = report.AccountTeamNotes ?? "";
                    ws.Cell("D14").Value = report.ManagementNotes ?? "";
                    ws.Cell("D15").Value = report.ManagementFeedback ?? "";
                    ws.Cell("D16").Value = report.Summary ?? "";

                    // ── Expense line items (rows 20–29) ────────
                    int row = 20;
                    int sno = 1;
                    foreach (var c in report.ExpenseClaims.OrderBy(x => x.ExpenseDate))
                    {
                        if (row > 29) break; // template has 10 slots

                        ws.Cell(row, 1).Value = sno++;
                        ws.Cell(row, 2).Value = c.Title;
                        ws.Cell(row, 3).Value = c.Category;
                        ws.Cell(row, 4).Value = c.PaymentMethod == PaymentMethod.UPI ? "UPI / Online" : "Cash";
                        ws.Cell(row, 5).Value = c.Amount;
                        ws.Cell(row, 6).Value = !string.IsNullOrEmpty(c.ReceiptPath) ? "Yes" : "No";
                        ws.Cell(row, 7).Value = c.Description ?? "";
                        ws.Cell(row, 8).Value = c.AccountTeamRemarks ?? "";
                        row++;
                    }

                    // E30 already has =SUM(E20:E29) in the template

                    // ── Signature names ────────────────────────
                    ws.Cell("A34").Value = report.Employee?.FullName ?? "";
                    ws.Cell("D34").Value = report.AccountTeamVerifiedBy?.FullName ?? "";
                    ws.Cell("G34").Value = report.ManagementReviewedBy?.FullName ?? "";

                    wb.SaveAs(xlMs);
                }

                // Add filled xlsx to ZIP
                xlMs.Seek(0, SeekOrigin.Begin);
                var xlEntry = archive.CreateEntry($"Report_{report.Id}_{Safe(report.ReportTitle)}.xlsx");
                using (var xlOut = xlEntry.Open())
                    await xlMs.CopyToAsync(xlOut);

                // Attach bill photos / PDFs in a Bills/ subfolder
                foreach (var c in report.ExpenseClaims.Where(x => !string.IsNullOrEmpty(x.ReceiptPath)))
                {
                    var fullPath = Path.Combine(webRoot, c.ReceiptPath!.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        var ext = Path.GetExtension(fullPath);
                        var safeName = Safe(c.Title);
                        var billEntry = archive.CreateEntry($"Bills/{c.ExpenseDate:yyyyMMdd}_{safeName}{ext}");
                        using var bs = billEntry.Open();
                        using var fs = System.IO.File.OpenRead(fullPath);
                        await fs.CopyToAsync(bs);
                    }
                }
            }

            ms.Seek(0, SeekOrigin.Begin);
            return File(
                ms.ToArray(),
                "application/zip",
                $"ExpenseReport_{Safe(report.ReportTitle)}_{DateTime.Now:yyyyMMdd}.zip"
            );
        }

        // ─── NOTIFICATIONS ────────────────────────────────────
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
