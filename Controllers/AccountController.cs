using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.ViewModels;

namespace ExpenseManagement.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        public AccountController(AppDbContext db) => _db = db;

        [HttpGet]
        public IActionResult Login() => View();

        [HttpPost]
        public async Task<IActionResult> Login(LoginVM model)
        {
            if (!ModelState.IsValid) return View(model);
            var user = await _db.Users.Include(u => u.Company).FirstOrDefaultAsync(u => u.Email == model.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(model.Password, user.PasswordHash))
            {
                TempData["Error"] = "Invalid email or password.";
                return View(model);
            }
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("CompanyId", user.CompanyId?.ToString() ?? ""),
                new Claim("CompanyName", user.Company?.Name ?? "")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            TempData["JustLoggedIn"] = true;

            return user.Role switch
            {
                UserRole.Employee =>
                    RedirectToAction("Dashboard", "HRMEmployee"),

                UserRole.AccountTeam =>
                    RedirectToAction("SelectProfile", "AccountTeam"),

                UserRole.Management =>
                    RedirectToAction("SelectDirector", "Management"),

                UserRole.HR =>
                    RedirectToAction("SelectProfile", "HR"),

                _ =>
                    RedirectToAction("Login")
            };
        }

        public async Task<IActionResult> Logout()
        {
            // Clear profile sessions before sign-out so stale names never leak to the next login
            HttpContext.Session.Clear();
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}
