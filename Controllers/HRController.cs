using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Data;
using ExpenseManagement.Models;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "HR")]
    public class HRController : Controller
    {
        private readonly AppDbContext _db;

        public HRController(AppDbContext db)
        {
            _db = db;
        }

        // Current logged-in HR user's company
        private int? CurrentCompanyId
        {
            get
            {
                var companyId = User.FindFirst("CompanyId")?.Value;

                if (int.TryParse(companyId, out var id))
                    return id;

                return null;
            }
        }

        // GET: /HR/SelectProfile
        [HttpGet]
        public async Task<IActionResult> SelectProfile()
        {
            var companyId = CurrentCompanyId;

            if (!companyId.HasValue)
                return Forbid();

            var profiles = await _db.PortalProfiles
                .Where(p =>
                    p.Role == UserRole.HR &&
                    p.CompanyId == companyId.Value &&
                    p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return View(profiles);
        }

        // POST: /HR/SelectProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SelectProfile(int profileId)
        {
            var companyId = CurrentCompanyId;

            if (!companyId.HasValue)
                return Forbid();

            // IMPORTANT:
            // The selected profile must belong to the same company
            // as the logged-in HR account.
            var profile = await _db.PortalProfiles
                .FirstOrDefaultAsync(p =>
                    p.Id == profileId &&
                    p.Role == UserRole.HR &&
                    p.CompanyId == companyId.Value &&
                    p.IsActive);

            if (profile == null)
            {
                TempData["Error"] = "Invalid HR profile selected.";
                return RedirectToAction(nameof(SelectProfile));
            }

            HttpContext.Session.SetString(
                "HRProfileName",
                profile.Name
            );

            HttpContext.Session.SetString(
                "HRProfileId",
                profile.Id.ToString()
            );

            return RedirectToAction("Dashboard", "HR");
        }

        // GET: /HR/Dashboard
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var companyId = CurrentCompanyId;

            if (!companyId.HasValue)
                return Forbid();

            var employeeCount = await _db.Users
                .AsNoTracking()
                .CountAsync(u =>
                    u.Role == UserRole.Employee &&
                    u.CompanyId == companyId.Value);

            ViewBag.EmployeeCount = employeeCount;

            ViewBag.CompanyName =
                User.FindFirst("CompanyName")?.Value ?? "";

            ViewBag.HRProfile =
                HttpContext.Session.GetString("HRProfileName") ?? "";

            return View();
        }

        // ─────────────────────────────────────────────────────────────
        // HR PROFILE MANAGEMENT
        // ─────────────────────────────────────────────────────────────

        // GET: /HR/ManageProfiles
        [HttpGet]
        public async Task<IActionResult> ManageProfiles()
        {
            var companyId = CurrentCompanyId;

            if (!companyId.HasValue)
                return Forbid();

            var profiles = await _db.PortalProfiles
                .Where(p =>
                    p.Role == UserRole.HR &&
                    p.CompanyId == companyId.Value &&
                    p.IsActive)
                .OrderBy(p => p.Name)
                .ToListAsync();

            ViewBag.CompanyName =
                User.FindFirst("CompanyName")?.Value ?? "";

            return View(profiles);
        }


        // POST: /HR/AddProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProfile(string name)
        {
            var companyId = CurrentCompanyId;

            if (!companyId.HasValue)
                return Forbid();

            name = name?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Name cannot be empty.";
                return RedirectToAction(nameof(ManageProfiles));
            }

            var exists = await _db.PortalProfiles
                .AnyAsync(p =>
                    p.Name == name &&
                    p.Role == UserRole.HR &&
                    p.CompanyId == companyId.Value &&
                    p.IsActive);

            if (exists)
            {
                TempData["Error"] = $"'{name}' already exists.";
                return RedirectToAction(nameof(ManageProfiles));
            }

            var profile = new PortalProfile
            {
                Name = name,
                Role = UserRole.HR,
                CompanyId = companyId.Value,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.PortalProfiles.Add(profile);

            await _db.SaveChangesAsync();

            TempData["Success"] = $"HR profile '{name}' added successfully.";

            return RedirectToAction(nameof(ManageProfiles));
        }


        // POST: /HR/DeleteProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProfile(int id)
        {
            var companyId = CurrentCompanyId;

            if (!companyId.HasValue)
                return Forbid();

            var profile = await _db.PortalProfiles
                .FirstOrDefaultAsync(p =>
                    p.Id == id &&
                    p.Role == UserRole.HR &&
                    p.CompanyId == companyId.Value &&
                    p.IsActive);

            if (profile == null)
            {
                TempData["Error"] = "HR profile not found.";
                return RedirectToAction(nameof(ManageProfiles));
            }

            // If currently selected HR profile is being deleted,
            // clear it from the session.
            var selectedProfileId =
                HttpContext.Session.GetString("HRProfileId");

            if (selectedProfileId == profile.Id.ToString())
            {
                HttpContext.Session.Remove("HRProfileId");
                HttpContext.Session.Remove("HRProfileName");
            }

            // Soft delete instead of physically deleting the record.
            profile.IsActive = false;

            await _db.SaveChangesAsync();

            TempData["Success"] =
                $"HR profile '{profile.Name}' removed successfully.";

            return RedirectToAction(nameof(ManageProfiles));
        }


        // POST: /HR/SwitchProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SwitchProfile()
        {
            HttpContext.Session.Remove("HRProfileId");
            HttpContext.Session.Remove("HRProfileName");

            return RedirectToAction(nameof(SelectProfile));
        }
    }
}