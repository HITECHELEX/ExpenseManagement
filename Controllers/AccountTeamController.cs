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
using ExpenseManagement.Services;
using ExpenseManagement.Helpers;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "AccountTeam")]
    public class AccountTeamController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly S3Service _s3;

        public AccountTeamController(AppDbContext db, IWebHostEnvironment env, S3Service s3)
        {
            _db = db;
            _env = env;
            _s3 = s3;
        }

        private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private int? CurrentCompanyId
        {
            get
            {
                var val = User.FindFirstValue("CompanyId");
                return string.IsNullOrEmpty(val) ? null : int.Parse(val);
            }
        }
        private string? SelectedATProfile => HttpContext.Session.GetString("ATProfileName");

        // ── Force AT profile selection before any AT page ────────────────────
        // Also validates that the session profile actually belongs to THIS company —
        // prevents stale sessions from a previous login (different company AT user
        // on the same browser) leaking across logins.
        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            var action = context.RouteData.Values["action"]?.ToString();
            if (action != null && (
                action.Equals("SelectProfile",   StringComparison.OrdinalIgnoreCase) ||
                action.Equals("SwitchProfile",   StringComparison.OrdinalIgnoreCase) ||
                action.Equals("ManageProfiles",  StringComparison.OrdinalIgnoreCase) ||
                action.Equals("AddProfile",      StringComparison.OrdinalIgnoreCase) ||
                action.Equals("DeleteProfile",   StringComparison.OrdinalIgnoreCase)))
                return;

            var sessionProfile = SelectedATProfile;
            if (string.IsNullOrEmpty(sessionProfile))
            {
                context.Result = RedirectToAction("SelectProfile");
                return;
            }

            // Validate the stored profile ID actually belongs to this company
            var storedIdStr = HttpContext.Session.GetString("ATProfileId");
            if (int.TryParse(storedIdStr, out var storedId))
            {
                var valid = _db.PortalProfiles.Any(p =>
                    p.Id == storedId &&
                    p.Role == UserRole.AccountTeam &&
                    p.CompanyId == CurrentCompanyId &&
                    p.IsActive);

                if (!valid)
                {
                    // Stale session from another company — clear and force re-selection
                    HttpContext.Session.Remove("ATProfileName");
                    HttpContext.Session.Remove("ATProfileId");
                    context.Result = RedirectToAction("SelectProfile");
                }
            }
            else
            {
                // No valid ID stored — clear and re-select
                HttpContext.Session.Remove("ATProfileName");
                context.Result = RedirectToAction("SelectProfile");
            }
        }

        // ── AT Profile Selection ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> SelectProfile()
        {
            if (!string.IsNullOrEmpty(SelectedATProfile))
                return RedirectToAction("Dashboard");

            var profiles = await _db.PortalProfiles
                .Where(p => p.Role == UserRole.AccountTeam && p.CompanyId == CurrentCompanyId && p.IsActive)
                .OrderBy(p => p.Name).ToListAsync();

            ViewBag.Profiles = profiles;
            ViewBag.CompanyName = User.FindFirstValue("CompanyName") ?? "";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectProfile(int profileId)
        {
            var profile = await _db.PortalProfiles
                .FirstOrDefaultAsync(p => p.Id == profileId && p.Role == UserRole.AccountTeam
                                         && p.CompanyId == CurrentCompanyId && p.IsActive);
            if (profile == null)
            {
                TempData["Error"] = "Invalid profile selected.";
                return RedirectToAction("SelectProfile");
            }
            HttpContext.Session.SetString("ATProfileName", profile.Name);
            HttpContext.Session.SetString("ATProfileId",   profile.Id.ToString());
            return RedirectToAction("Dashboard", "AccountTeam");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SwitchProfile()
        {
            HttpContext.Session.Remove("ATProfileName");
            HttpContext.Session.Remove("ATProfileId");
            return RedirectToAction("SelectProfile");
        }

        // ── Manage Profiles (Add / Delete) ──────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ManageProfiles()
        {
            var profiles = await _db.PortalProfiles
                .Where(p => p.Role == UserRole.AccountTeam && p.CompanyId == CurrentCompanyId)
                .OrderBy(p => p.Name).ToListAsync();
            ViewBag.CompanyName = User.FindFirstValue("CompanyName") ?? "";
            return View(profiles);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProfile(string name)
        {
            name = name?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            { TempData["Error"] = "Name cannot be empty."; return RedirectToAction("ManageProfiles"); }

            var exists = await _db.PortalProfiles.AnyAsync(p =>
                p.Name == name && p.Role == UserRole.AccountTeam && p.CompanyId == CurrentCompanyId);
            if (exists)
            { TempData["Error"] = $"'{name}' already exists."; return RedirectToAction("ManageProfiles"); }

            _db.PortalProfiles.Add(new PortalProfile
            {
                Name = name, Role = UserRole.AccountTeam,
                CompanyId = CurrentCompanyId, IsActive = true, CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Profile '{name}' added.";
            return RedirectToAction("ManageProfiles");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProfile(int id)
        {
            var profile = await _db.PortalProfiles
                .FirstOrDefaultAsync(p => p.Id == id && p.Role == UserRole.AccountTeam && p.CompanyId == CurrentCompanyId);
            if (profile != null)
            {
                _db.PortalProfiles.Remove(profile);
                await _db.SaveChangesAsync();
                TempData["Success"] = $"Profile '{profile.Name}' removed.";
                // Clear session if this was the active profile
                if (HttpContext.Session.GetString("ATProfileName") == profile.Name)
                {
                    HttpContext.Session.Remove("ATProfileName");
                    HttpContext.Session.Remove("ATProfileId");
                }
            }
            return RedirectToAction("ManageProfiles");
        }

        // ─── DASHBOARD ────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            var companyId = CurrentCompanyId;

            var employeeIds = await _db.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
                .Select(u => u.Id).ToListAsync();

            var pendingReports = await _db.ExpenseReports
                .AsNoTracking()
                .Include(r => r.Employee)
                .Include(r => r.ExpenseClaims)
                .Where(r => r.Status == ReportStatus.SubmittedToAccountTeam && employeeIds.Contains(r.EmployeeId))
                .Take(5).ToListAsync();

            var recentTopUps = await _db.WalletTopUps
                .AsNoTracking()
                .Include(w => w.Employee)
                .Include(w => w.TopUpBy)
                .Where(w => employeeIds.Contains(w.EmployeeId))
                .OrderByDescending(w => w.TopUpOn)
                .Take(10).ToListAsync();

            var unread = await _db.Notifications
                .CountAsync(n => n.UserId == CurrentUserId && !n.IsRead);

            var companyName = User.FindFirstValue("CompanyName") ?? "";

            var vm = new AccountDashboardVM
            {
                PendingClaims     = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Pending && employeeIds.Contains(c.EmployeeId)),
                ApprovedClaims    = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Approved && employeeIds.Contains(c.EmployeeId)),
                DeclinedClaims    = await _db.ExpenseClaims.CountAsync(c => c.Status == ExpenseStatus.Declined && employeeIds.Contains(c.EmployeeId)),
                PendingReports    = await _db.ExpenseReports.CountAsync(r => r.Status == ReportStatus.SubmittedToAccountTeam && employeeIds.Contains(r.EmployeeId)),
                TotalEmployees    = employeeIds.Count,
                TotalAllocated    = await _db.Budgets.Where(b => employeeIds.Contains(b.EmployeeId)).SumAsync(b => b.TotalAmount),
                TotalApproved     = await _db.ExpenseClaims.Where(c => c.Status == ExpenseStatus.Approved && employeeIds.Contains(c.EmployeeId)).SumAsync(c => c.Amount),
                TotalTopUps       = await _db.WalletTopUps.CountAsync(w => employeeIds.Contains(w.EmployeeId)),
                TotalTopUpAmount  = await _db.WalletTopUps.Where(w => employeeIds.Contains(w.EmployeeId)).SumAsync(w => w.Amount),
                PendingReports_List = pendingReports,
                RecentTopUps      = recentTopUps,
                UnreadNotifications = unread,
                CompanyName       = companyName
            };

            return View(vm);
        }

        // ─── TOP UP WALLET ────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> AllocateBudget()
        {
            var companyId = CurrentCompanyId;
            var employees = await _db.Users.Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
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
            var companyId = CurrentCompanyId;
            var employees = await _db.Users.Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
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

            // Use the user-provided transfer date; fall back to now if blank
            DateTime transferUtc = model.TransferDate.HasValue
                ? DateTime.SpecifyKind(model.TransferDate.Value, DateTimeKind.Utc)
                : DateTime.UtcNow;

            decimal newBalance;
            if (wallet != null)
            {
                // Top up: just add to TotalAmount — RemainingBalance corrects automatically
                wallet.TotalAmount    += model.TotalAmount;
                wallet.Purpose         = model.Purpose;
                wallet.AllocatedById   = CurrentUserId;
                wallet.AllocatedOn     = transferUtc;
                wallet.OverspentAmount = 0;
                if (model.ValidUntil.HasValue)
                    wallet.ValidUntil = model.ValidUntil;
                newBalance = wallet.RemainingBalance;
            }
            else
            {
                // First top-up ever — create the wallet
                wallet = new Budget
                {
                    EmployeeId      = model.EmployeeId,
                    AllocatedById   = CurrentUserId,
                    TotalAmount     = model.TotalAmount,
                    SpentAmount     = 0,
                    ReturnedBalance = 0,
                    OverspentAmount = 0,
                    Purpose         = model.Purpose,
                    ValidUntil      = model.ValidUntil,
                    AllocatedOn     = transferUtc,
                    IsActive        = true,
                    IsLocked        = false
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
                CreatedAt = DateTime.UtcNow
            });

            _db.WalletTopUps.Add(new WalletTopUp
            {
                EmployeeId   = model.EmployeeId,
                TopUpById    = CurrentUserId,
                Amount       = model.TotalAmount,
                Purpose      = model.Purpose,
                TopUpOn      = transferUtc,     // ← recorded transfer date
                BalanceAfter = newBalance
            });

            _db.WalletTransactions.Add(new WalletTransaction
            {
                EmployeeId    = model.EmployeeId,
                PerformedById = CurrentUserId,
                Type          = WalletTransactionType.Credit,
                Amount        = model.TotalAmount,
                Description   = $"Wallet Top-up: {model.Purpose}",
                Date          = transferUtc,    // ← recorded transfer date
                BalanceAfter  = newBalance
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = $"₹{model.TotalAmount:N2} added to {employee.FullName}'s wallet. New balance: ₹{newBalance:N2}.";
            return RedirectToAction("Budgets");
        }

        // ─── OPENING / CARRY-FORWARD BALANCE ─────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddOpeningBalance(
            int EmployeeId, decimal Amount, string Period,
            string? Remarks, IFormFile? ProofDocument)
        {
            // --- Reload view data for error returns ---
            var companyId = CurrentCompanyId;
            async Task<IActionResult> ReloadView(string error)
            {
                var employees2 = await _db.Users
                    .Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
                    .Include(u => u.Budgets).ToListAsync();
                var walletMap2 = new Dictionary<int, decimal>();
                foreach (var e in employees2)
                    walletMap2[e.Id] = e.Budgets.Where(b => b.IsActive && !b.IsLocked).Sum(b => b.RemainingBalance);
                ViewBag.Employees = employees2;
                ViewBag.WalletMap = walletMap2;
                ModelState.AddModelError("", error);
                return View("AllocateBudget", new ExpenseManagement.ViewModels.AllocateBudgetVM());
            }

            if (EmployeeId == 0)              return await ReloadView("Please select an employee.");
            if (Amount == 0)                  return await ReloadView("Amount cannot be zero.");
            if (string.IsNullOrWhiteSpace(Period)) return await ReloadView("Period is required.");
            if (ProofDocument == null || ProofDocument.Length == 0)
                return await ReloadView("Proof document is required for an opening balance entry.");

            var employee = await _db.Users.FindAsync(EmployeeId);
            if (employee == null || employee.Role != UserRole.Employee)
                return await ReloadView("Invalid employee.");

            // Find or create the active budget for this employee
            var budget = await _db.Budgets
                .Where(b => b.EmployeeId == EmployeeId && b.IsActive && !b.IsLocked)
                .OrderByDescending(b => b.AllocatedOn)
                .FirstOrDefaultAsync();

            decimal absAmount = Math.Abs(Amount);

            if (budget == null)
            {
                // Create a budget with the opening balance already applied — NO early SaveChanges.
                // EF Core resolves the BudgetId FK via the Budget navigation property in one transaction.
                budget = new Budget
                {
                    EmployeeId    = EmployeeId,
                    AllocatedById = CurrentUserId,
                    TotalAmount   = Amount > 0 ? absAmount : 0,   // Credit sets TotalAmount
                    SpentAmount   = Amount < 0 ? absAmount : 0,   // Debit sets SpentAmount
                    Purpose       = $"Opening Balance: {Period}",
                    AllocatedOn   = DateTime.UtcNow,
                    IsActive      = true,
                    IsLocked      = false
                };
                _db.Budgets.Add(budget);
            }
            else
            {
                // Existing budget — adjust accordingly
                if (Amount > 0)
                    budget.TotalAmount += absAmount;   // Credit: boosts available balance
                else
                    budget.SpentAmount += absAmount;   // Debit:  reduces available balance
            }

            decimal balanceAfter = budget.RemainingBalance;

            // ── Upload proof document to S3 ────────────────────────────────────
            string? proofPath = null;
            string? proofFileName = null;
            try
            {
                var ext      = Path.GetExtension(ProofDocument.FileName).ToLower();
                var s3Key    = $"opening-balance/{EmployeeId}/{Guid.NewGuid()}{ext}";
                using var stream = ProofDocument.OpenReadStream();
                proofPath     = await _s3.UploadAsync(stream, s3Key, ProofDocument.ContentType);
                proofFileName = ProofDocument.FileName;
            }
            catch
            {
                return await ReloadView("Failed to upload proof document. Please try again.");
            }

            // ── Create OpeningBalance audit record ─────────────────────────────
            // Use navigation property "Budget = budget" so EF resolves BudgetId automatically —
            // this is safe even when budget is newly created (Id still 0 before SaveChanges).
            _db.OpeningBalances.Add(new OpeningBalance
            {
                EmployeeId          = EmployeeId,
                AddedById           = CurrentUserId,
                Budget              = budget,   // EF resolves BudgetId from this navigation property
                Amount              = Amount,
                Period              = Period,
                Remarks             = Remarks,
                ProofDocumentPath   = proofPath,
                ProofDocumentFileName = proofFileName,
                AddedOn             = DateTime.UtcNow
            });

            // ── WalletTransaction for history display ──────────────────────────
            _db.WalletTransactions.Add(new WalletTransaction
            {
                EmployeeId    = EmployeeId,
                PerformedById = CurrentUserId,
                Type          = Amount > 0 ? WalletTransactionType.Credit : WalletTransactionType.Debit,
                Amount        = absAmount,
                Description   = $"Opening Balance ({Period}){(string.IsNullOrWhiteSpace(Remarks) ? "" : ": " + Remarks)}",
                Date          = DateTime.UtcNow,
                BalanceAfter  = balanceAfter
            });

            // ── Notify employee ────────────────────────────────────────────────
            string sign = Amount > 0 ? "credited" : "debited";
            _db.Notifications.Add(new Notification
            {
                UserId    = EmployeeId,
                Message   = $"Opening balance of ₹{absAmount:N2} {sign} for period: {Period}. New balance: ₹{balanceAfter:N2}.",
                Link      = "/Employee/MyBudgets",
                Icon      = "balance-scale",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Opening balance of {(Amount > 0 ? "+" : "-")}₹{absAmount:N2} applied for {employee.FullName} ({Period}). New balance: ₹{balanceAfter:N2}.";
            return RedirectToAction("AllocateBudget");
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
            var companyId = CurrentCompanyId;
            var budgets = await _db.Budgets
                .Include(b => b.Employee)
                .Include(b => b.AllocatedBy)
                .Where(b => companyId == null || b.Employee!.CompanyId == companyId)
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

            var transactions = await _db.WalletTransactions
                .Include(t => t.PerformedBy)
                .Where(t => t.EmployeeId == id)
                .OrderByDescending(t => t.Date)
                .ToListAsync();

            var openingBalances = await _db.OpeningBalances
                .Include(o => o.AddedBy)
                .Where(o => o.EmployeeId == id)
                .OrderByDescending(o => o.AddedOn)
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
                Transactions     = transactions,
                OpeningBalances  = openingBalances,
                Budgets          = employee.Budgets.OrderByDescending(b => b.AllocatedOn).ToList()
            };

            return View(vm);
        }

        // ─── EMPLOYEES ────────────────────────────────────────
        public async Task<IActionResult> Employees()
        {
            var companyId = CurrentCompanyId;
            var employees = await _db.Users
                .Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
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
                CompanyId = CurrentCompanyId,
                CreatedAt = DateTime.UtcNow
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
        public async Task<IActionResult> AllocationBreakdown(string? month)
        {
            var companyId = CurrentCompanyId;
            var allBudgets = await _db.Budgets
                .AsNoTracking()
                .Include(b => b.Employee)
                .Where(b => companyId == null || b.Employee!.CompanyId == companyId)
                .OrderBy(b => b.Employee!.FullName)
                .ToListAsync();

            var employeeIds = allBudgets.Select(b => b.EmployeeId).Distinct().ToList();

            var allTopUps = await _db.WalletTopUps
                .AsNoTracking()
                .Include(w => w.Employee)
                .Where(w => employeeIds.Contains(w.EmployeeId))
                .OrderByDescending(w => w.TopUpOn)
                .ToListAsync();

            // Build available months from IST AllocatedOn dates
            var availableMonths = allBudgets
                .Select(b => b.AllocatedOn.ToIst())
                .Select(d => new { Key = d.ToString("yyyy-MM"), Label = d.ToString("MMM yyyy") })
                .DistinctBy(m => m.Key)
                .OrderByDescending(m => m.Key)
                .ToList();

            // Filter by selected month (IST-aware)
            List<Budget> budgets = allBudgets;
            List<WalletTopUp> topUps = allTopUps;
            if (!string.IsNullOrEmpty(month) && DateTime.TryParseExact(month + "-01", "yyyy-MM-dd",
                    null, System.Globalization.DateTimeStyles.None, out var sel))
            {
                budgets = allBudgets
                    .Where(b => { var d = b.AllocatedOn.ToIst(); return d.Year == sel.Year && d.Month == sel.Month; })
                    .ToList();
                topUps = allTopUps
                    .Where(t => { var d = t.TopUpOn.ToIst(); return d.Year == sel.Year && d.Month == sel.Month; })
                    .ToList();
            }

            ViewBag.SelectedMonth   = month ?? "";
            ViewBag.AvailableMonths = availableMonths;
            ViewBag.IsMonthlyView   = !string.IsNullOrEmpty(month);

            var reports = await _db.ExpenseReports.AsNoTracking().ToListAsync();

            // Employee name/dept lookup from all budgets
            var empInfoLookup = allBudgets
                .GroupBy(b => b.EmployeeId)
                .ToDictionary(g => g.Key, g => g.First().Employee);

            List<ExpenseManagement.ViewModels.EmployeeAllocationRow> byEmployee;

            if (!string.IsNullOrEmpty(month))
            {
                // ── MONTH SELECTED: build from top-ups so totals match "By Month" ──
                // Include any employee who received a top-up OR had a budget allocated this month
                var monthEmpIds = topUps.Select(t => t.EmployeeId)
                    .Union(budgets.Select(b => b.EmployeeId))
                    .Distinct().ToList();

                byEmployee = monthEmpIds
                    .Select(empId =>
                    {
                        var empTopUps  = topUps.Where(t => t.EmployeeId == empId).ToList();
                        var empBudgets = budgets.Where(b => b.EmployeeId == empId).ToList();
                        var emp        = empInfoLookup.GetValueOrDefault(empId)
                                         ?? allTopUps.FirstOrDefault(t => t.EmployeeId == empId)?.Employee;
                        var topUpAmt   = empTopUps.Sum(t => t.Amount);
                        return new ExpenseManagement.ViewModels.EmployeeAllocationRow
                        {
                            EmployeeId     = empId,
                            EmployeeName   = emp?.FullName ?? "Unknown",
                            Department     = emp?.Department ?? "",
                            TotalAllocated = topUpAmt,
                            TotalSpent     = empBudgets.Sum(b => b.SpentAmount),
                            Remaining      = empBudgets.Sum(b => b.RemainingBalance),
                            TopUpAmount    = topUpAmt,
                            TopUpCount     = empTopUps.Count,
                            BudgetCount    = empBudgets.Count,
                            ReportCount    = reports.Count(r => r.EmployeeId == empId)
                        };
                    })
                    .Where(e => e.TotalAllocated > 0)
                    .OrderByDescending(e => e.TotalAllocated)
                    .ToList();
            }
            else
            {
                // ── ALL MONTHS: budget-based view (existing behaviour) ──
                byEmployee = budgets
                    .GroupBy(b => b.EmployeeId)
                    .Select(g =>
                    {
                        var empTopUps = allTopUps.Where(t => t.EmployeeId == g.Key).ToList();
                        return new ExpenseManagement.ViewModels.EmployeeAllocationRow
                        {
                            EmployeeId     = g.Key,
                            EmployeeName   = g.First().Employee?.FullName ?? "",
                            Department     = g.First().Employee?.Department ?? "",
                            TotalAllocated = g.Sum(b => b.TotalAmount),
                            TotalSpent     = g.Sum(b => b.SpentAmount),
                            Remaining      = g.Sum(b => b.RemainingBalance),
                            TopUpAmount    = empTopUps.Sum(t => t.Amount),
                            TopUpCount     = empTopUps.Count,
                            BudgetCount    = g.Count(),
                            ReportCount    = reports.Count(r => r.EmployeeId == g.Key)
                        };
                    })
                    .OrderByDescending(e => e.TotalAllocated)
                    .ToList();
            }

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

            // For monthly view, banner shows top-up totals (matches By Month + By Employee totals)
            bool isMonthly = !string.IsNullOrEmpty(month);
            var vm = new ExpenseManagement.ViewModels.AllocationBreakdownVM
            {
                TotalAllocated = isMonthly ? topUps.Sum(t => t.Amount) : budgets.Sum(b => b.TotalAmount),
                TotalSpent     = budgets.Sum(b => b.SpentAmount),
                TotalRemaining = isMonthly ? topUps.Sum(t => t.Amount) - budgets.Sum(b => b.SpentAmount)
                                           : budgets.Sum(b => b.RemainingBalance),
                ByEmployee     = byEmployee,
                ByMonth        = byMonth,
                AllBudgets     = budgets.OrderByDescending(b => b.AllocatedOn).ToList()
            };

            return View(vm);
        }

        // ─── REVIEW REPORTS ───────────────────────────────────
        public async Task<IActionResult> Reports(string? status, int? employeeId)
        {
            var companyId = CurrentCompanyId;

            // Load employees for dropdown (company-scoped)
            var employees = await _db.Users
                .AsNoTracking()
                .Where(u => u.Role == UserRole.Employee && (companyId == null || u.CompanyId == companyId))
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName, u.Department })
                .ToListAsync();

            ViewBag.Employees        = employees;
            ViewBag.SelectedEmployee = employeeId;
            ViewBag.CurrentStatus    = status ?? "SubmittedToAccountTeam";

            var query = _db.ExpenseReports
                .Include(r => r.Employee)
                .Include(r => r.Budget)
                .Where(r => companyId == null || r.Employee!.CompanyId == companyId)
                .AsQueryable();

            // Filter by employee if selected
            if (employeeId.HasValue && employeeId > 0)
                query = query.Where(r => r.EmployeeId == employeeId.Value);

            // Filter by status
            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ReportStatus>(status, out var s))
                query = query.Where(r => r.Status == s);
            else
                query = query.Where(r => r.Status == ReportStatus.SubmittedToAccountTeam);

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
        public async Task<IActionResult> ReviewExpense(int reportId, int claimId, string reviewAction, string? feedback, string? remark)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.ExpenseClaims)
                .FirstOrDefaultAsync(r => r.Id == reportId && r.Status == ReportStatus.SubmittedToAccountTeam);

            if (report == null) return NotFound();

            var claim = report.ExpenseClaims.FirstOrDefault(c => c.Id == claimId);
            if (claim == null) return NotFound();

            claim.ReviewedById = CurrentUserId;
            claim.ReviewedOn = DateTime.UtcNow;

            if (reviewAction == "approve")
            {
                claim.Status = ExpenseStatus.Approved;
                // Save remark even on approval so notes aren't lost
                if (!string.IsNullOrWhiteSpace(remark))
                    claim.AccountTeamRemarks = remark.Trim();
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
        public async Task<IActionResult> SaveExpenseRemark(int reportId, int claimId, string? remark)
        {
            var report = await _db.ExpenseReports
                .Include(r => r.ExpenseClaims)
                .FirstOrDefaultAsync(r => r.Id == reportId);
            if (report == null) return NotFound();
            var claim = report.ExpenseClaims.FirstOrDefault(c => c.Id == claimId);
            if (claim == null) return NotFound();
            claim.AccountTeamRemarks = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
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
            report.AccountTeamVerifiedOn = DateTime.UtcNow;
            report.TotalApproved = report.ExpenseClaims.Sum(c => c.Amount);
            report.AccountTeamProfileName = SelectedATProfile;

            var atLabel = SelectedATProfile ?? "Account Team";
            string overspendNote = report.RemainingBalanceAtSubmission < 0
                ? $" ⚠️ Overspent by ₹{Math.Abs(report.RemainingBalanceAtSubmission):N2}"
                : "";

            var mgmt = await _db.Users.Where(u => u.Role == UserRole.Management).ToListAsync();
            foreach (var m in mgmt)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = m.Id,
                    Message = $"Expense report '{report.ReportTitle}' from {report.Employee!.FullName} is ready for review (reviewed by {atLabel}).{overspendNote}",
                    Link = $"/Management/ReportDetail/{report.Id}",
                    Icon = "tasks",
                    CreatedAt = DateTime.UtcNow
                });
            }

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' was reviewed by {atLabel} and forwarded to Management.",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = "check-circle",
                CreatedAt = DateTime.UtcNow
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
            report.AccountTeamProfileName = SelectedATProfile;
            report.AccountTeamVerifiedOn = DateTime.UtcNow;
            report.RemainingBalanceAtSubmission = 0;

            _db.Notifications.Add(new Notification
            {
                UserId = report.EmployeeId,
                Message = $"Your report '{report.ReportTitle}' was rejected by Account Team. Reason: {rejectionReason}. Please review and resubmit.",
                Link = $"/Employee/ReportDetail/{report.Id}",
                Icon = "times-circle",
                CreatedAt = DateTime.UtcNow
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
                    ws.Cell("H8").Value  = !string.IsNullOrEmpty(report.AccountTeamProfileName)
                        ? report.AccountTeamProfileName
                        : (report.AccountTeamVerifiedBy?.FullName ?? "Account Team");
                    ws.Cell("C9").Value  = report.TotalClaimed;
                    ws.Cell("H9").Value  = !string.IsNullOrEmpty(report.DirectorName)
                        ? report.DirectorName
                        : (report.ManagementReviewedBy?.FullName ?? "Management");
                    // Expenses Receive Date (row 10)
                    if (report.FundsReceivedOn.HasValue)
                    {
                        ws.Cell("A10").Value = "Expenses Receive Date";
                        ws.Cell("B10").Value = report.FundsReceivedOn.Value.ToIst().ToString("dd/MM/yyyy");
                    }
                    else
                    {
                        ws.Cell("A10").Value = "";
                    }
                    ws.Cell("C10").Value = "";

                    // ── Review & approval notes ────────────────
                    ws.Cell("D13").Value = report.AccountTeamNotes ?? "";
                    ws.Cell("D14").Value = ""; // ManagementNotes is internal — never exported
                    ws.Cell("D15").Value = report.ManagementFeedback ?? "";
                    ws.Cell("D16").Value = report.Summary ?? "";

                    // ── Expense Date header (col 9) ────────────
                    ws.Cell(19, 9).Value = "Expense Date";
                    ws.Cell(19, 9).Style.Font.Bold = true;
                    ws.Cell(19, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F3864");
                    ws.Cell(19, 9).Style.Font.FontColor = XLColor.White;
                    ws.Cell(19, 9).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    // ── Expense line items — unlimited rows, bulk insert ───
                    // Template has 10 data rows (20-29), total at 30, signature at 34.
                    // Strategy: insert ALL extra rows at once before writing data —
                    // this is O(1) vs the old O(n²) one-row-at-a-time loop.
                    const int FIRST_DATA_ROW    = 20;
                    const int TEMPLATE_DATA_ROWS = 10;   // rows 20-29
                    const int TEMPLATE_TOTAL_ROW = 30;
                    const int TEMPLATE_SIG_ROW   = 34;

                    var sortedClaims = report.ExpenseClaims.OrderBy(x => x.ExpenseDate).ToList();
                    int claimCount   = sortedClaims.Count;

                    // Step 1 — bulk-insert extra rows ONCE (much faster than per-row inserts)
                    if (claimCount > TEMPLATE_DATA_ROWS)
                    {
                        int extraRows = claimCount - TEMPLATE_DATA_ROWS;
                        // Insert after the last template data row (row 29) in a single call
                        ws.Row(FIRST_DATA_ROW + TEMPLATE_DATA_ROWS - 1).InsertRowsBelow(extraRows);

                        // Apply the same cell style as row 20 to every newly inserted row
                        // — do this col-by-col once per new row (acceptable for 500+ rows)
                        for (int r = FIRST_DATA_ROW + TEMPLATE_DATA_ROWS;
                             r < FIRST_DATA_ROW + claimCount; r++)
                        {
                            for (int col = 1; col <= 9; col++)
                                ws.Cell(r, col).Style = ws.Cell(FIRST_DATA_ROW, col).Style;
                        }
                    }

                    // Step 2 — write all claim data sequentially (no checks inside loop)
                    for (int i = 0; i < claimCount; i++)
                    {
                        var c   = sortedClaims[i];
                        int row = FIRST_DATA_ROW + i;

                        ws.Cell(row, 1).Value = i + 1;   // S.No
                        ws.Cell(row, 2).Value = c.Title ?? "";
                        ws.Cell(row, 3).Value = c.Category ?? "";
                        ws.Cell(row, 4).Value = c.PaymentMethod == PaymentMethod.UPI
                            ? "UPI / Online" : "Cash";
                        ws.Cell(row, 5).Value = c.Amount;
                        ws.Cell(row, 5).Style.NumberFormat.Format = "₹#,##0.00";
                        ws.Cell(row, 6).Value = !string.IsNullOrEmpty(c.ReceiptPath) ? "Yes" : "No";
                        ws.Cell(row, 7).Value = c.Description ?? "";
                        ws.Cell(row, 8).Value = c.AccountTeamRemarks ?? "";
                        ws.Cell(row, 9).Value = c.ExpenseDate.ToIst().ToString("dd MMM yyyy");
                    }

                    // Step 3 — compute actual positions using template-native rows.
                    // The template TOTAL is at row 30 for ≤10 claims; for >10 claims
                    // InsertRowsBelow already pushed it to exactly FIRST_DATA_ROW + claimCount.
                    // Using this formula guarantees we update the EXISTING template rows
                    // rather than creating duplicate rows above them.
                    int lastDataRow    = FIRST_DATA_ROW + claimCount - 1;
                    int actualTotalRow = FIRST_DATA_ROW + Math.Max(TEMPLATE_DATA_ROWS, claimCount);
                    int actualSigRow   = actualTotalRow + 4;  // template sig-names row (A34 for ≤10)

                    // Step 3b — clear empty data rows between last claim and TOTAL
                    // (for ≤10 claims: rows 21-29 would otherwise show blank styled cells)
                    for (int r = lastDataRow + 1; r < actualTotalRow; r++)
                        for (int c = 1; c <= 9; c++)
                            ws.Cell(r, c).Value = "";

                    // Step 4 — update TOTAL formula in the existing template TOTAL row.
                    // The template already has the dark-blue styling; we only refresh the formula.
                    ws.Cell(actualTotalRow, 5).FormulaA1 =
                        $"=SUM(E{FIRST_DATA_ROW}:E{lastDataRow})";
                    ws.Cell(actualTotalRow, 5).Style.NumberFormat.Format = "₹#,##0.00";

                    // Step 5 — resolve reviewer / director names
                    var atName  = !string.IsNullOrEmpty(report.AccountTeamProfileName)
                        ? report.AccountTeamProfileName
                        : (report.AccountTeamVerifiedBy?.FullName ?? "Account Team");
                    var dirName = !string.IsNullOrEmpty(report.DirectorName)
                        ? report.DirectorName
                        : (report.ManagementReviewedBy?.FullName ?? "Management");
                    var empName = report.Employee?.FullName ?? "";

                    // Step 6 — write names into the existing template signature row.
                    // The template signature structure (header, labels, sig lines) is already
                    // in place; we only overwrite the name cells.
                    ws.Cell(actualSigRow, 1).Value = empName;
                    ws.Cell(actualSigRow, 4).Value = atName;
                    ws.Cell(actualSigRow, 7).Value = dirName;

                    wb.SaveAs(xlMs);
                }

                // Add filled xlsx to ZIP
                xlMs.Seek(0, SeekOrigin.Begin);
                var xlEntry = archive.CreateEntry($"Report_{report.Id}_{Safe(report.ReportTitle)}.xlsx");
                using (var xlOut = xlEntry.Open())
                    await xlMs.CopyToAsync(xlOut);

                // Attach bill photos / PDFs in a Bills/ subfolder — fetched from S3
                foreach (var c in report.ExpenseClaims.Where(x => !string.IsNullOrEmpty(x.ReceiptPath)))
                {
                    try
                    {
                        var (s3Stream, _, ext) = await _s3.DownloadAsync(c.ReceiptPath!);
                        var safeName  = Safe(c.Title);
                        var safeAmt   = ((int)c.Amount).ToString();
                        var safeDate  = c.ExpenseDate.ToIst().ToString("yyyyMMdd");
                        var billEntry = archive.CreateEntry($"Bills/{safeName}_{safeAmt}_{safeDate}{ext}");
                        using var bs = billEntry.Open();
                        await s3Stream.CopyToAsync(bs);
                        await s3Stream.DisposeAsync();
                    }
                    catch
                    {
                        // If a receipt is missing from S3, skip it and continue building the ZIP
                    }
                }
            }

            ms.Seek(0, SeekOrigin.Begin);
            return File(
                ms.ToArray(),
                "application/zip",
                $"ExpenseReport_{Safe(report.ReportTitle)}_{DateTime.UtcNow:yyyyMMdd}.zip"
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
