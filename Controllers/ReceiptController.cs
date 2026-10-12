using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ExpenseManagement.Services;

namespace ExpenseManagement.Controllers
{
    /// <summary>
    /// Shared receipt viewer — accessible to any authenticated user (Employee, AccountTeam, Management).
    /// Generates a 60-minute presigned S3 URL and redirects to it.
    /// </summary>
    [Authorize]
    public class ReceiptController : Controller
    {
        private readonly S3Service _s3;
        public ReceiptController(S3Service s3) => _s3 = s3;

        /// <summary>
        /// GET /Receipt/View?key=receipts/abc123.jpg
        /// Generates a presigned URL and redirects the browser directly to S3.
        /// </summary>
        [HttpGet]
        public IActionResult View(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return BadRequest("Receipt key is missing.");
            var url = _s3.GetPresignedUrl(key, expiryMinutes: 60);
            return Redirect(url);
        }
    }
}
