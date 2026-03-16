using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IO.Compression;
using System.Text;
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
            var pending = await _db.ExpenseClaims
                .Include(c => c.Employee).Include(c => c.Budget)
                .Where(c => c.Status == ExpenseStatus.Pending)
                .OrderByDescending(c => c.SubmittedOn).Take(5).ToListAsync();

            var pendingReports = await _db.ExpenseReports
                .Include(r => r.Employee)
                .Where(r => r.Status == ReportStatus.SubmittedToAccountTeam)
                .Take(5).ToListAsync();

            var unread = await _db.Notifications
                .CountAsync(n => n.UserId == CurrentUserId && !n.IsRead);

            var awaitingTransfer = await _db.ExpenseReports
                .CountAsync(r => r.Status == ReportStatus.ApprovedByManagement
                    && !r.BalanceTransferred
                    && r.RemainingBalanceAtSubmission > 0);

            var vm = new AccountDashboardVM
            {
                PendingClaims = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Pending),
                ApprovedClaims = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Approved),
                DeclinedClaims = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Declined),
                PendingReports = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.SubmittedToAccountTeam),
                TotalAllocated = await _db.Budgets.SumAsync(b => b.TotalAmount),
                TotalApproved = await _db.ExpenseClaims.Where(c => c.Status == ExpenseStatus.Approved).SumAsync(c => c.Amount),
                RecentPendingClaims = pending,
                PendingReports_List = pendingReports,
                UnreadNotifications = unread,
                AwaitingTransfer = awaitingTransfer
            };

            return View(vm);
        }

        // ─── ALLOCATE BUDGET — adds to employee's global wallet ──
        // If employee has an active unlocked budget → add amount to it (increase TotalAmount)
        // If no active budget → create a new one (first allocation or after full lock)
        // Overspend debt is deducted from the newly available amount first.
        [HttpGet]
        public async Task<IActionResult> AllocateBudget()
        {
            var employees = await _db.Users.Where(u => u.Role == UserRole.Employee).OrderBy(u => u.FullName).ToListAsync();
            ViewBag.Employees = employees;

            // Build wallet summary per employee
            var walletMap = new Dictionary<int, (decimal Balance, decimal Owed, bool HasActive)>();
            foreach (var emp in employees)
            {
                var budgets = await _db.Budgets.Where(b => b.EmployeeId == emp.Id && b.IsActive && !b.IsLocked).ToListAsync();
                decimal bal = budgets.Sum(b => b.RemainingBalance);
                decimal owed = await _db.Budgets.Where(b => b.EmployeeId == emp.Id && b.OverspentAmount > 0).SumAsync(b => b.OverspentAmount);
                walletMap[emp.Id] = (bal, owed, budgets.Any());
            }
            ViewBag.WalletMap = walletMap;
            return View(new AllocateBudgetVM());
        }

        [HttpPost]
        public async Task<IActionResult> AllocateBudget(AllocateBudgetVM model)
        {
            var employees = await _db.Users.Where(u => u.Role == UserRole.Employee).OrderBy(u => u.FullName).ToListAsync();
            ViewBag.Employees = employees;
            var walletMap = new Dictionary<int, (decimal Balance, decimal Owed, bool HasActive)>();
            foreach (var emp in employees)
            {
                var bds = await _db.Budgets.Where(b => b.EmployeeId == emp.Id && b.IsActive && !b.IsLocked).ToListAsync();
                decimal bl = bds.Sum(b => b.RemainingBalance);
                decimal ow = await _db.Budgets.Where(b => b.EmployeeId == emp.Id && b.OverspentAmount > 0).SumAsync(b => b.OverspentAmount);
                walletMap[emp.Id] = (bl, ow, bds.Any());
            }
            ViewBag.WalletMap = walletMap;

            if (!ModelState.IsValid) return View(model);

            var employee = await _db.Users.FindAsync(model.EmployeeId);
            if (employee == null || employee.Role != UserRole.Employee)
            { ModelState.AddModelError("", "Invalid employee."); return View(model); }

            // ── Deduct crystallised overspend first ──
            var allBudgets = await _db.Budgets.Where(b => b.EmployeeId == model.EmployeeId).ToListAsync();
            decimal totalOwed = allBudgets.Where(b => b.OverspentAmount > 0).Sum(b => b.OverspentAmount);
            decimal deducted = 0;

            if (totalOwed > 0)
            {
                deducted = Math.Min(totalOwed, model.TotalAmount);
                decimal remaining = deducted;
                foreach (var b in allBudgets.Where(x => x.OverspentAmount > 0).OrderBy(x => x.AllocatedOn))
                {
                    if (remaining <= 0) break;
                    if (b.OverspentAmount <= remaining) { remaining -= b.OverspentAmount; b.OverspentAmount = 0; }
                    else { b.OverspentAmount -= remaining; remaining = 0; }
                }
            }

            decimal available = model.TotalAmount - deducted;

            // ── Top up existing active wallet budget, or create new ──
            var activeBudget = allBudgets.FirstOrDefault(b => b.IsActive && !b.IsLocked);

            if (activeBudget != null)
            {
                // Add to existing budget: increase TotalAmount (SpentAmount stays, so balance increases)
                activeBudget.TotalAmount += available;
                // The deducted amount acts as pre-spent on the existing budget
                activeBudget.SpentAmount += deducted;
            }
            else
            {
                // Create new wallet budget
                var newBudget = new Budget
                {
                    EmployeeId = model.EmployeeId,
                    AllocatedById = CurrentUserId,
                    TotalAmount = model.TotalAmount,
                    SpentAmount = deducted,   // pre-charged for overspend
                    OverspentAmount = 0,
                    Purpose = model.Purpose,
                    ValidUntil = model.ValidUntil,
                    AllocatedOn = DateTime.Now,
                    IsActive = true,
                    IsLocked = false
                };
                _db.Budgets.Add(newBudget);
            }

            string notif = deducted > 0
                ? $"₹{model.TotalAmount:N2} added to your wallet for '{model.Purpose}'. ₹{deducted:N2} deducted for previous overspend. Net available: ₹{available:N2}."
                : $"₹{model.TotalAmount:N2} added to your wallet for '{model.Purpose}'. Total available balance updated.";

            _db.Notifications.Add(new Notification
            {
                UserId = employee.Id,
                Message = notif,
                Link = "/Employee/MyBudgets",
                Icon = "wallet",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = deducted > 0
                ? $"₹{model.TotalAmount:N2} allocated to {employee.FullName}. ₹{deducted:N2} overspend deducted. Net wallet top-up: ₹{available:N2}."
                : $"₹{model.TotalAmount:N2} added to {employee.FullName}'s wallet.";
            return RedirectToAction("Budgets");
        }

        // ─── BUDGETS ──────────────────────────────────────────
        public async Task<IActionResult> Budgets()
        {
            var budgets = await _db.Budgets
                .Include(b => b.Employee).Include(b => b.AllocatedBy)
                .OrderByDescending(b => b.AllocatedOn).ToListAsync();
            return View(budgets);
        }

        // ─── EMPLOYEES ─────────────────────────────────────────
        public async Task<IActionResult> Employees()
        {
            var employees = await _db.Users
                .Where(u => u.Role == UserRole.Employee)
                .Include(u => u.Budgets)
                .OrderBy(u => u.FullName)
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
            var emp = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee);
            if (emp == null) return NotFound();
            return View(new EditEmployeeVM
            {
                Id = emp.Id,
                FullName = emp.FullName,
                Email = emp.Email,
                Department = emp.Department
            });
        }

        [HttpPost]
        public async Task<IActionResult> EditEmployee(EditEmployeeVM model)
        {
            if (!ModelState.IsValid) return View(model);

            var emp = await _db.Users.FirstOrDefaultAsync(u => u.Id == model.Id && u.Role == UserRole.Employee);
            if (emp == null) return NotFound();

            // Check email uniqueness (excluding self)
            if (await _db.Users.AnyAsync(u => u.Email == model.Email && u.Id != model.Id))
            {
                ModelState.AddModelError("Email", "This email is already used by another account.");
                return View(model);
            }

            emp.FullName = model.FullName;
            emp.Email = model.Email;
            emp.Department = model.Department;

            if (!string.IsNullOrWhiteSpace(model.NewPassword))
                emp.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.NewPassword);

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Employee '{emp.FullName}' updated.";
            return RedirectToAction("Employees");
        }

        [HttpPost]
        public async Task<IActionResult> DeleteEmployee(int id)
        {
            var emp = await _db.Users
                .Include(u => u.Budgets)
                .Include(u => u.ExpenseReports)
                .FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee);

            if (emp == null) return NotFound();

            // Only allow deletion if employee has no submitted/approved reports
            bool hasActiveReports = await _db.ExpenseReports
                .AnyAsync(r => r.EmployeeId == id &&
                               r.Status != ReportStatus.Draft &&
                               r.Status != ReportStatus.RejectedByManagement);

            if (hasActiveReports)
            {
                TempData["Error"] = $"Cannot delete '{emp.FullName}' — they have active or pending reports. Resolve those first.";
                return RedirectToAction("Employees");
            }

            // Delete notifications
            var notifs = await _db.Notifications.Where(n => n.UserId == id).ToListAsync();
            _db.Notifications.RemoveRange(notifs);

            // Delete draft reports & their claims
            var draftReports = await _db.ExpenseReports
                .Include(r => r.ExpenseClaims)
                .Where(r => r.EmployeeId == id && r.Status == ReportStatus.Draft)
                .ToListAsync();

            foreach (var r in draftReports)
                _db.ExpenseClaims.RemoveRange(r.ExpenseClaims);
            _db.ExpenseReports.RemoveRange(draftReports);

            // Delete budgets (only if no claims attached)
            var budgets = await _db.Budgets.Where(b => b.EmployeeId == id).ToListAsync();
            _db.Budgets.RemoveRange(budgets);

            _db.Users.Remove(emp);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Employee '{emp.FullName}' deleted.";
            return RedirectToAction("Employees");
        }

        // ─── EMPLOYEE DETAIL (budget history + balance) ────────
        public async Task<IActionResult> EmployeeDetail(int id)
        {
            var emp = await _db.Users
                .Include(u => u.Budgets).ThenInclude(b => b.AllocatedBy)
                .FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Employee);
            if (emp == null) return NotFound();

            var reports = await _db.ExpenseReports
                .Include(r => r.Budget)
                .Where(r => r.EmployeeId == id)
                .OrderByDescending(r => r.CreatedOn)
                .ToListAsync();

            ViewBag.Reports = reports;
            return View(emp);
        }

        // ─── REPORTS ──────────────────────────────────────────
        public async Task<IActionResult> Reports(string? status)
        {
            var query = _db.ExpenseReports.Include(r => r.Employee).Include(r => r.Budget).AsQueryable();
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
                .Include(r => r.TransferredToBudget)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (report == null) return NotFound();
            return View(report);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveReport(int reportId, string? notes)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.SubmittedToAccountTeam);
            if (report == null) return NotFound();

            report.Status = ReportStatus.ReviewedByAccountTeam;
            report.AccountTeamNotes = notes;
            report.AccountTeamVerifiedById = CurrentUserId;
            report.AccountTeamVerifiedOn = DateTime.Now;

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' was approved by Account Team and is awaiting forwarding to Management.",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = "check-circle",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = "Report approved. You can now forward it to Management.";
            return RedirectToAction("ReportDetail", new { id = reportId });
        }

        [HttpPost]
        public async Task<IActionResult> RejectReport(int reportId, string? feedback)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.SubmittedToAccountTeam);
            if (report == null) return NotFound();

            report.Status = ReportStatus.Draft;
            report.AccountTeamNotes = feedback;

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' was returned for revision. Reason: {feedback}",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = "times-circle",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();
            TempData["Warning"] = $"Report returned to {report.Employee?.FullName} for revision.";
            return RedirectToAction("Reports");
        }

        [HttpPost]
        public async Task<IActionResult> ForwardToManagement(int reportId, string? notes)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.ExpenseClaims).Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.ReviewedByAccountTeam);

            if (report == null) return NotFound();

            report.Status = ReportStatus.ForwardedToManagement;
            report.AccountTeamNotes = notes;
            report.AccountTeamVerifiedById = CurrentUserId;
            report.AccountTeamVerifiedOn = DateTime.Now;
            report.TotalApproved = report.ExpenseClaims.Sum(c => c.Amount);

            string overspendNote = report.RemainingBalanceAtSubmission < 0
                ? $" ⚠️ Overspent ₹{Math.Abs(report.RemainingBalanceAtSubmission):N2}"
                : "";

            var mgmt = await _db.Users.Where(u => u.Role == UserRole.Management).ToListAsync();
            foreach (var m in mgmt)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = m.Id,
                    Message = $"Report '{report.ReportTitle}' from {report.Employee!.FullName} ready for review.{overspendNote}",
                    Link = $"/Management/ReportDetail/{report.Id}",
                    Icon = "tasks",
                    CreatedAt = DateTime.Now
                });
            }
            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' has been reviewed and forwarded to Management.",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = "check-circle",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = "Report forwarded to Management.";
            return RedirectToAction("Reports");
        }

        // ─── TRANSFER BALANCE ─────────────────────────────────
        // After Management approves, account team sends the returned balance to a budget.
        // If the report had overspend (RemainingBalanceAtSubmission < 0), there's nothing
        // to transfer — instead we crystallise the OverspentAmount on the budget.
        [HttpGet]
        public async Task<IActionResult> TransferBalance(int reportId)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId
                    && r.Status == ReportStatus.ApprovedByManagement
                    && !r.BalanceTransferred);

            if (report == null)
            {
                TempData["Error"] = "Report not eligible for this action.";
                return RedirectToAction("Reports", new { status = "ApprovedByManagement" });
            }

            // If overspent: show confirmation page (POST will crystallise)
            if (report.RemainingBalanceAtSubmission <= 0)
            {
                ViewBag.Report = report;
                ViewBag.Budgets = new List<Budget>();
                return View();
            }

            var budgets = await _db.Budgets
                .Where(b => b.EmployeeId == report.EmployeeId && b.IsActive && !b.IsLocked)
                .OrderByDescending(b => b.AllocatedOn)
                .ToListAsync();

            ViewBag.Report = report;
            ViewBag.Budgets = budgets;
            return View();
        }

        // Crystallise overspend without a balance to return (when report was overspent)
        private async Task<IActionResult> CrystalliseOverspend(ExpenseReport report)
        {
            decimal overspent = Math.Abs(report.RemainingBalanceAtSubmission);

            if (report.Budget != null)
            {
                // Record the overspend on the budget so it can be deducted from next allocation
                report.Budget.OverspentAmount += overspent;
                report.Budget.IsLocked = true;
                report.Budget.LockedReason = $"Report '{report.ReportTitle}' approved. Overspent ₹{overspent:N2} — will be deducted from next allocation.";
            }

            report.BalanceTransferred = true;  // Mark as "settled" (no balance to actually move)
            report.BalanceTransferredOn = DateTime.Now;

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Report '{report.ReportTitle}' approved. You overspent by ₹{overspent:N2} — this will be automatically deducted from your next budget allocation.",
                Link = "/Employee/MyBudgets",
                Icon = "exclamation-triangle",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Overspend of ₹{overspent:N2} recorded. Will be deducted from {report.Employee?.FullName ?? "employee"}'s next allocation.";
            return RedirectToAction("ReportDetail", new { id = report.Id });
        }

        [HttpPost]
        public async Task<IActionResult> TransferBalance(int reportId, string transferMode,
            int? existingBudgetId, string? newBudgetPurpose, DateTime? newBudgetValidUntil)
        {
            // Handle overspend crystallisation (no balance to return)
            if (transferMode == "overspend")
            {
                var ovReport = await _db.ExpenseReports
                    .Include(r => r.Employee).Include(r => r.Budget)
                    .FirstOrDefaultAsync(r => r.Id == reportId
                        && r.Status == ReportStatus.ApprovedByManagement
                        && !r.BalanceTransferred);
                if (ovReport == null) { TempData["Error"] = "Report not found or already settled."; return RedirectToAction("ReportDetail", new { id = reportId }); }
                return await CrystalliseOverspend(ovReport);
            }

            var report = await _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.Budget)
                .FirstOrDefaultAsync(r => r.Id == reportId
                    && r.Status == ReportStatus.ApprovedByManagement
                    && !r.BalanceTransferred
                    && r.RemainingBalanceAtSubmission > 0);

            if (report == null)
            {
                TempData["Error"] = "Invalid report or balance already processed.";
                return RedirectToAction("ReportDetail", new { id = reportId });
            }

            decimal balanceToReturn = report.RemainingBalanceAtSubmission;
            int targetBudgetId;
            string targetPurpose;

            if (transferMode == "existing" && existingBudgetId.HasValue)
            {
                var targetBudget = await _db.Budgets.FindAsync(existingBudgetId);
                if (targetBudget == null || targetBudget.EmployeeId != report.EmployeeId)
                {
                    TempData["Error"] = "Invalid budget selected.";
                    return RedirectToAction("TransferBalance", new { reportId });
                }
                // Credit balance back by reducing SpentAmount
                targetBudget.SpentAmount -= balanceToReturn;
                if (targetBudget.SpentAmount < 0) targetBudget.SpentAmount = 0;
                targetBudgetId = targetBudget.Id;
                targetPurpose = targetBudget.Purpose;
                TempData["Success"] = $"₹{balanceToReturn:N2} added to budget '{targetPurpose}'.";
            }
            else
            {
                // Create a NEW budget with the returned amount
                string purpose = string.IsNullOrWhiteSpace(newBudgetPurpose)
                    ? $"Returned – {report.ReportTitle}"
                    : newBudgetPurpose.Trim();

                var newBudget = new Budget
                {
                    EmployeeId = report.EmployeeId,
                    AllocatedById = CurrentUserId,
                    TotalAmount = balanceToReturn,
                    SpentAmount = 0,
                    ReturnedBalance = 0,
                    OverspentAmount = 0,
                    Purpose = purpose,
                    ValidUntil = newBudgetValidUntil,
                    AllocatedOn = DateTime.Now,
                    IsActive = true,
                    IsLocked = false
                };
                _db.Budgets.Add(newBudget);
                await _db.SaveChangesAsync();
                targetBudgetId = newBudget.Id;
                targetPurpose = purpose;
                TempData["Success"] = $"New budget '{purpose}' of ₹{balanceToReturn:N2} created for {report.Employee!.FullName}.";
            }

            // Lock the source budget — report cycle complete, no new reports against it
            if (report.Budget != null)
            {
                report.Budget.IsLocked = true;
                report.Budget.LockedReason = $"Report '{report.ReportTitle}' approved & ₹{balanceToReturn:N2} returned on {DateTime.Now:dd MMM yyyy}.";
            }

            report.BalanceTransferred = true;
            report.BalanceTransferredOn = DateTime.Now;
            report.TransferredToBudgetId = targetBudgetId;

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"₹{balanceToReturn:N2} from approved report '{report.ReportTitle}' has been returned as budget '{targetPurpose}'. Your previous budget has been locked.",
                Link = "/Employee/MyBudgets",
                Icon = "wallet",
                CreatedAt = DateTime.Now
            });

            await _db.SaveChangesAsync();
            return RedirectToAction("ReportDetail", new { id = reportId });
        }

        // ─── DOWNLOAD ZIP ─────────────────────────────────────
        public async Task<IActionResult> DownloadReport(int id)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.Employee).Include(r => r.Budget)
                .Include(r => r.ExpenseClaims)
                .Include(r => r.AccountTeamVerifiedBy)
                .Include(r => r.ManagementReviewedBy)
                .FirstOrDefaultAsync(r => r.Id == id && r.Status == ReportStatus.ApprovedByManagement);

            if (report == null) { TempData["Error"] = "Report not found or not yet approved."; return RedirectToAction("Reports"); }

            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var csv = new StringBuilder();
                csv.AppendLine("Expense Report - " + report.ReportTitle);
                csv.AppendLine($"Employee,{report.Employee?.FullName}");
                csv.AppendLine($"Department,{report.Employee?.Department}");
                csv.AppendLine($"Budget,{report.Budget?.Purpose}");
                csv.AppendLine($"Total Budget,Rs.{report.Budget?.TotalAmount:N2}");
                csv.AppendLine($"Total Claimed,Rs.{report.TotalClaimed:N2}");
                if (report.RemainingBalanceAtSubmission >= 0)
                    csv.AppendLine($"Returned Balance,Rs.{report.RemainingBalanceAtSubmission:N2}");
                else
                    csv.AppendLine($"Overspent,Rs.{Math.Abs(report.RemainingBalanceAtSubmission):N2}");
                csv.AppendLine($"Status,Approved");
                csv.AppendLine($"Forwarded On,{report.ForwardedToAccountTeamOn:dd/MM/yyyy HH:mm}");
                csv.AppendLine($"Reviewed By,{report.AccountTeamVerifiedBy?.FullName}");
                csv.AppendLine($"Approved By,{report.ManagementReviewedBy?.FullName}");
                csv.AppendLine($"Notes,{report.ManagementNotes}");
                csv.AppendLine();
                csv.AppendLine("S.No,Date,Title,Category,Description,Amount,Bill,Status");
                int sno = 1;
                foreach (var c in report.ExpenseClaims.OrderBy(x => x.ExpenseDate))
                    csv.AppendLine($"{sno++},{c.ExpenseDate:dd/MM/yyyy},\"{c.Title}\",{c.Category},\"{c.Description}\",Rs.{c.Amount:N2},{(!string.IsNullOrEmpty(c.ReceiptPath) ? "Yes" : "No")},{c.Status}");

                var csvEntry = archive.CreateEntry($"Report_{report.Id}.csv");
                using (var w = new StreamWriter(csvEntry.Open(), Encoding.UTF8)) w.Write(csv.ToString());

                foreach (var c in report.ExpenseClaims.Where(x => !string.IsNullOrEmpty(x.ReceiptPath)))
                {
                    var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
                    var fullPath = Path.Combine(webRoot, c.ReceiptPath!.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        var ext = Path.GetExtension(fullPath);
                        var safe = string.Concat(c.Title.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                        var be = archive.CreateEntry($"Bills/{c.ExpenseDate:yyyyMMdd}_{safe}{ext}");
                        using var bs = be.Open(); using var fs = System.IO.File.OpenRead(fullPath);
                        await fs.CopyToAsync(bs);
                    }
                }
            }

            ms.Seek(0, SeekOrigin.Begin);
            var safeTitle = string.Concat(report.ReportTitle.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            return File(ms.ToArray(), "application/zip", $"Report_{safeTitle}_{DateTime.Now:yyyyMMdd}.zip");
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
