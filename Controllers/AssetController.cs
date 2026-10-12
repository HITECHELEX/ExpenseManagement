using ExpenseManagement.Data;
using ExpenseManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "HR")]
    public class AssetController : Controller
    {
        private readonly AppDbContext _db;

        public AssetController(AppDbContext db)
        {
            _db = db;
        }

        // ============================================================
        // CURRENT USER / COMPANY
        // ============================================================

        private int CurrentCompanyId =>
            int.Parse(User.FindFirst("CompanyId")?.Value ?? "0");

        private int CurrentUserId =>
            int.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");

        // ============================================================
        // HR - ASSET DASHBOARD / LIST
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search,
            string? status,
            string? assetType)
        {
            var query = _db.Assets
                .AsNoTracking()
                .Include(a => a.Company)
                .Include(a => a.CurrentEmployee)
                    .ThenInclude(e => e!.User)
                .Where(a => a.CompanyId == CurrentCompanyId);

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();

                query = query.Where(a =>
                    a.AssetCode.Contains(search) ||
                    a.AssetName.Contains(search) ||
                    (a.AssetType != null &&
                     a.AssetType.Contains(search)) ||
                    (a.SerialNumber != null &&
                     a.SerialNumber.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(
                    a => a.Status == status.Trim());
            }

            if (!string.IsNullOrWhiteSpace(assetType))
            {
                query = query.Where(
                    a => a.AssetType == assetType.Trim());
            }

            var assets = await query
                .OrderBy(a => a.AssetName)
                .ThenBy(a => a.AssetCode)
                .ToListAsync();

            ViewBag.AssetPage = "Index";

            ViewBag.Search = search;
            ViewBag.Status = status;
            ViewBag.AssetType = assetType;

            ViewBag.TotalAssets =
                await _db.Assets.CountAsync(
                    a => a.CompanyId == CurrentCompanyId);

            ViewBag.AvailableAssets =
                await _db.Assets.CountAsync(
                    a =>
                        a.CompanyId == CurrentCompanyId &&
                        a.Status == "Available");

            ViewBag.AssignedAssets =
                await _db.Assets.CountAsync(
                    a =>
                        a.CompanyId == CurrentCompanyId &&
                        a.Status == "Assigned");

            ViewBag.MaintenanceAssets =
                await _db.Assets.CountAsync(
                    a =>
                        a.CompanyId == CurrentCompanyId &&
                        a.Status == "Under Maintenance");

            ViewBag.AssetTypes =
                await _db.Assets
                    .AsNoTracking()
                    .Where(a =>
                        a.CompanyId == CurrentCompanyId &&
                        a.AssetType != null &&
                        a.AssetType != "")
                    .Select(a => a.AssetType!)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToListAsync();

            return View(
                "~/Views/Asset/AssetHR.cshtml",
                assets);
        }

        // ============================================================
        // HR - CREATE ASSET
        // ============================================================

        [HttpGet]
        public IActionResult Create()
        {
            ViewBag.AssetPage = "Create";

            return View(
                "~/Views/Asset/AssetHR.cshtml",
                new Asset());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Asset model)
        {
            RemoveLifecycleValidation();
            Normalize(model);

            var duplicate = await _db.Assets.AnyAsync(a =>
                a.CompanyId == CurrentCompanyId &&
                a.AssetCode == model.AssetCode);

            if (duplicate)
            {
                ModelState.AddModelError(
                    nameof(Asset.AssetCode),
                    "This asset code is already in use for this company.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.AssetPage = "Create";

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    model);
            }

            // Company is always taken from the logged-in HR user.
            model.CompanyId = CurrentCompanyId;

            // New asset must start as Available.
            model.CurrentEmployeeId = null;
            model.AssignedOn = null;
            model.ExpectedReturnDate = null;
            model.ReturnedOn = null;
            model.ConditionOnAssignment = null;
            model.ConditionOnReturn = null;
            model.Status = "Available";

            model.CreatedAt = DateTime.UtcNow;
            model.UpdatedAt = DateTime.UtcNow;

            await using var transaction =
                await _db.Database.BeginTransactionAsync();

            try
            {
                _db.Assets.Add(model);

                await _db.SaveChangesAsync();

                AddAudit(
                    "Created",
                    model.Id,
                    $"Asset '{model.AssetCode}' " +
                    $"({model.AssetName}) was created.");

                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                TempData["Success"] =
                    $"Asset '{model.AssetCode}' created successfully.";

                return RedirectToAction(nameof(Index));
            }
            catch
            {
                await transaction.RollbackAsync();

                ModelState.AddModelError(
                    "",
                    "Unable to create the asset. " +
                    "No changes were saved.");

                ViewBag.AssetPage = "Create";

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    model);
            }
        }

        // ============================================================
        // HR - EDIT ASSET
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var asset = await GetAsset(id);

            if (asset == null)
                return NotFound();

            ViewBag.AssetPage = "Edit";

            return View(
                "~/Views/Asset/AssetHR.cshtml",
                asset);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            Asset model)
        {
            var asset = await GetAsset(id);

            if (asset == null)
                return NotFound();

            RemoveLifecycleValidation();
            Normalize(model);

            var duplicate = await _db.Assets.AnyAsync(a =>
                a.CompanyId == CurrentCompanyId &&
                a.AssetCode == model.AssetCode &&
                a.Id != id);

            if (duplicate)
            {
                ModelState.AddModelError(
                    nameof(Asset.AssetCode),
                    "This asset code is already in use for this company.");
            }

            if (!ModelState.IsValid)
            {
                model.Id = id;

                ViewBag.AssetPage = "Edit";

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    model);
            }

            // Only master information is edited here.
            // Assignment lifecycle is handled by Assign / Return.

            asset.AssetCode = model.AssetCode;
            asset.AssetName = model.AssetName;
            asset.AssetType = model.AssetType;
            asset.SerialNumber = model.SerialNumber;
            asset.Description = model.Description;
            asset.Remarks = model.Remarks;
            asset.UpdatedAt = DateTime.UtcNow;

            await using var transaction =
                await _db.Database.BeginTransactionAsync();

            try
            {
                await _db.SaveChangesAsync();

                AddAudit(
                    "Updated",
                    asset.Id,
                    $"Asset '{asset.AssetCode}' " +
                    $"({asset.AssetName}) was updated.");

                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                TempData["Success"] =
                    $"Asset '{asset.AssetCode}' updated successfully.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }
            catch
            {
                await transaction.RollbackAsync();

                ModelState.AddModelError(
                    "",
                    "Unable to update the asset. " +
                    "No changes were saved.");

                ViewBag.AssetPage = "Edit";

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    model);
            }
        }

        // ============================================================
        // HR - ASSET DETAILS
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var asset = await GetAsset(id);

            if (asset == null)
                return NotFound();

            ViewBag.AssetPage = "Details";

            ViewBag.History =
                await _db.AssetHistories
                    .AsNoTracking()
                    .Include(h => h.Employee)
                        .ThenInclude(e => e!.User)
                    .Include(h => h.PerformedByUser)
                    .Where(h => h.AssetId == id)
                    .OrderByDescending(h => h.ActionDate)
                    .ThenByDescending(h => h.Id)
                    .ToListAsync();

            return View(
                "~/Views/Asset/AssetHR.cshtml",
                asset);
        }

        // ============================================================
        // HR - ASSIGN ASSET
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Assign(int id)
        {
            var asset = await GetAsset(id);

            if (asset == null)
                return NotFound();

            if (asset.Status != "Available" ||
                asset.CurrentEmployeeId.HasValue)
            {
                TempData["Error"] =
                    "Only an available, unassigned asset can be assigned.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }

            await LoadEmployees();

            ViewBag.AssetPage = "Assign";
            ViewBag.Asset = asset;

            return View(
                "~/Views/Asset/AssetHR.cshtml",
                asset);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Assign(
            int id,
            int employeeId,
            DateTime assignedOn,
            DateTime? expectedReturnDate,
            string? condition,
            string? remarks)
        {
            var asset = await GetAsset(id);

            if (asset == null)
                return NotFound();

            if (asset.Status != "Available" ||
                asset.CurrentEmployeeId.HasValue)
            {
                TempData["Error"] =
                    "Only an available, unassigned asset can be assigned.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }

            // IMPORTANT:
            // Employee must belong to the same company.
            var employee =
                await _db.Employees
                    .Include(e => e.User)
                    .FirstOrDefaultAsync(e =>
                        e.Id == employeeId &&
                        e.User.CompanyId == CurrentCompanyId &&
                        e.User.Role == UserRole.Employee &&
                        e.EmploymentStatus != "Inactive");

            if (employee == null)
            {
                await LoadEmployees();

                ViewBag.AssetPage = "Assign";
                ViewBag.Asset = asset;

                ModelState.AddModelError(
                    nameof(employeeId),
                    "Select a valid active employee from this company.");

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    asset);
            }

            if (assignedOn == default)
                assignedOn = DateTime.UtcNow;

            if (expectedReturnDate.HasValue &&
                expectedReturnDate.Value.Date < assignedOn.Date)
            {
                await LoadEmployees();

                ViewBag.AssetPage = "Assign";
                ViewBag.Asset = asset;

                ModelState.AddModelError(
                    nameof(expectedReturnDate),
                    "Expected return date cannot be earlier " +
                    "than the assignment date.");

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    asset);
            }

            await using var transaction =
                await _db.Database.BeginTransactionAsync();

            try
            {
                asset.CurrentEmployeeId = employee.Id;
                asset.AssignedOn = assignedOn;
                asset.ExpectedReturnDate = expectedReturnDate;
                asset.ReturnedOn = null;
                asset.Status = "Assigned";
                asset.ConditionOnAssignment = Clean(condition);
                asset.ConditionOnReturn = null;
                asset.UpdatedAt = DateTime.UtcNow;

                _db.AssetHistories.Add(
                    new AssetHistory
                    {
                        AssetId = asset.Id,
                        EmployeeId = employee.Id,
                        Action = "Assigned",
                        ActionDate = assignedOn,
                        Condition = Clean(condition),
                        Remarks = Clean(remarks),
                        PerformedBy = CurrentUserId,
                        CreatedAt = DateTime.UtcNow
                    });

                await _db.SaveChangesAsync();

                AddAudit(
                    "Assigned",
                    asset.Id,
                    $"Asset '{asset.AssetCode}' was assigned " +
                    $"to '{employee.User.FullName}'.");

                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                TempData["Success"] =
                    $"Asset '{asset.AssetCode}' assigned to " +
                    $"{employee.User.FullName}.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }
            catch
            {
                await transaction.RollbackAsync();

                await LoadEmployees();

                ViewBag.AssetPage = "Assign";
                ViewBag.Asset = asset;

                ModelState.AddModelError(
                    "",
                    "Unable to assign the asset. " +
                    "No changes were saved.");

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    asset);
            }
        }

        // ============================================================
        // HR - RETURN ASSET
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Return(int id)
        {
            var asset = await GetAsset(id);

            if (asset == null)
                return NotFound();

            if (asset.Status != "Assigned" ||
                !asset.CurrentEmployeeId.HasValue)
            {
                TempData["Error"] =
                    "This asset is not currently assigned.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }

            ViewBag.AssetPage = "Return";
            ViewBag.Asset = asset;

            return View(
                "~/Views/Asset/AssetHR.cshtml",
                asset);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Return(
            int id,
            DateTime returnedOn,
            string? condition,
            string? remarks)
        {
            var asset = await _db.Assets
                .Include(a => a.CurrentEmployee)
                    .ThenInclude(e => e!.User)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    a.CompanyId == CurrentCompanyId);

            if (asset == null)
                return NotFound();

            if (asset.Status != "Assigned" ||
                !asset.CurrentEmployeeId.HasValue)
            {
                TempData["Error"] =
                    "This asset is not currently assigned.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }

            if (returnedOn == default)
                returnedOn = DateTime.UtcNow;

            if (asset.AssignedOn.HasValue &&
                returnedOn.Date < asset.AssignedOn.Value.Date)
            {
                ViewBag.AssetPage = "Return";
                ViewBag.Asset = asset;

                ModelState.AddModelError(
                    nameof(returnedOn),
                    "Return date cannot be earlier than the assignment date.");

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    asset);
            }

            var previousEmployeeId =
                asset.CurrentEmployeeId;

            var previousEmployeeName =
                asset.CurrentEmployee?.User?.FullName
                ?? "Employee";

            await using var transaction =
                await _db.Database.BeginTransactionAsync();

            try
            {
                asset.CurrentEmployeeId = null;
                asset.ReturnedOn = returnedOn;
                asset.ExpectedReturnDate = null;
                asset.Status = "Available";
                asset.ConditionOnReturn = Clean(condition);
                asset.UpdatedAt = DateTime.UtcNow;

                _db.AssetHistories.Add(
                    new AssetHistory
                    {
                        AssetId = asset.Id,
                        EmployeeId = previousEmployeeId,
                        Action = "Returned",
                        ActionDate = returnedOn,
                        Condition = Clean(condition),
                        Remarks = Clean(remarks),
                        PerformedBy = CurrentUserId,
                        CreatedAt = DateTime.UtcNow
                    });

                await _db.SaveChangesAsync();

                AddAudit(
                    "Returned",
                    asset.Id,
                    $"Asset '{asset.AssetCode}' was returned " +
                    $"by '{previousEmployeeName}'.");

                await _db.SaveChangesAsync();

                await transaction.CommitAsync();

                TempData["Success"] =
                    $"Asset '{asset.AssetCode}' returned successfully.";

                return RedirectToAction(
                    nameof(Details),
                    new { id });
            }
            catch
            {
                await transaction.RollbackAsync();

                ViewBag.AssetPage = "Return";
                ViewBag.Asset = asset;

                ModelState.AddModelError(
                    "",
                    "Unable to return the asset. " +
                    "No changes were saved.");

                return View(
                    "~/Views/Asset/AssetHR.cshtml",
                    asset);
            }
        }

        // ============================================================
        // HR - ASSET HISTORY
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> History(int id)
        {
            var asset = await GetAsset(id);

            if (asset == null)
                return NotFound();

            ViewBag.AssetPage = "History";
            ViewBag.Asset = asset;

            var history =
                await _db.AssetHistories
                    .AsNoTracking()
                    .Include(h => h.Employee)
                        .ThenInclude(e => e!.User)
                    .Include(h => h.PerformedByUser)
                    .Where(h => h.AssetId == id)
                    .OrderByDescending(h => h.ActionDate)
                    .ThenByDescending(h => h.Id)
                    .ToListAsync();

            return View(
                "~/Views/Asset/AssetHR.cshtml",
                history);
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private async Task<Asset?> GetAsset(int id)
        {
            return await _db.Assets
                .Include(a => a.Company)
                .Include(a => a.CurrentEmployee)
                    .ThenInclude(e => e!.User)
                .FirstOrDefaultAsync(a =>
                    a.Id == id &&
                    a.CompanyId == CurrentCompanyId);
        }

        private async Task LoadEmployees()
        {
            ViewBag.Employees =
                await _db.Employees
                    .AsNoTracking()
                    .Include(e => e.User)
                    .Where(e =>
                        e.User.CompanyId == CurrentCompanyId &&
                        e.User.Role == UserRole.Employee &&
                        e.EmploymentStatus != "Inactive")
                    .OrderBy(e => e.User.FullName)
                    .ToListAsync();
        }

        private void AddAudit(
            string action,
            int assetId,
            string description)
        {
            _db.AuditLogs.Add(
                new AuditLog
                {
                    CompanyId = CurrentCompanyId,
                    UserId = CurrentUserId,
                    Action = action,
                    EntityName = "Asset",
                    EntityId = assetId,
                    Description = description,
                    CreatedAt = DateTime.UtcNow
                });
        }

        private static string? Clean(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }

        private static void Normalize(Asset model)
        {
            model.AssetCode =
                model.AssetCode?.Trim() ?? "";

            model.AssetName =
                model.AssetName?.Trim() ?? "";

            model.AssetType =
                Clean(model.AssetType);

            model.SerialNumber =
                Clean(model.SerialNumber);
        }

        private void RemoveLifecycleValidation()
        {
            foreach (var key in new[]
            {
                nameof(Asset.CompanyId),
                nameof(Asset.CurrentEmployeeId),
                nameof(Asset.AssignedOn),
                nameof(Asset.ExpectedReturnDate),
                nameof(Asset.ReturnedOn),
                nameof(Asset.ConditionOnAssignment),
                nameof(Asset.ConditionOnReturn),
                nameof(Asset.Status)
            })
            {
                ModelState.Remove(key);
            }
        }
    }
}