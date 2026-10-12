using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExpenseManagement.Models;

namespace ExpenseManagement.Controllers
{
    [Authorize]
    public class PortalController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult Expense()
        {
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            return role switch
            {
                nameof(UserRole.Employee)
                    => RedirectToAction("Dashboard", "Employee"),

                nameof(UserRole.AccountTeam)
                    => RedirectToAction("Dashboard", "AccountTeam"),

                nameof(UserRole.Management)
                    => RedirectToAction("Dashboard", "Management"),

                _ => Forbid()
            };
        }

        
        // HRM entry point
        [HttpGet]
        public IActionResult HRM()
        {
            var role = User.FindFirst(
                System.Security.Claims.ClaimTypes.Role)?.Value;

            return role switch
            {
                nameof(UserRole.Employee)
                    => RedirectToAction("Dashboard", "HRMEmployee"),

                nameof(UserRole.HR)
                    => RedirectToAction("Dashboard", "HR"),

                nameof(UserRole.Management)
                    => RedirectToAction("Dashboard", "HRMManagement"),

                _ => Forbid()
            };
        }
    }
}