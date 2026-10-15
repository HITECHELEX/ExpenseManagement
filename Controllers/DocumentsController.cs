using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using ExpenseManagement.Data;
using ExpenseManagement.Models;
using System.IO;

namespace ExpenseManagement.Controllers
{
    [Authorize(Roles = "Employee,HR")]
    public class DocumentsController : Controller
    {
        private readonly AppDbContext _db;

        public DocumentsController(AppDbContext db)
        {
            _db = db;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private int CurrentCompanyId =>
            int.Parse(User.FindFirstValue("CompanyId")!);


        // ============================================================
        // EMPLOYEE DOCUMENTS
        // GET: /Documents/Employee
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpGet]
        public async Task<IActionResult> Employee()
        {
            var employee = await _db.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            var documents = await _db.Documents
                .AsNoTracking()
                .Where(d =>
                    d.CompanyId == CurrentCompanyId &&
                    d.EmployeeId == employee.Id)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            return View("~/Views/Documents/Employee.cshtml", documents);
        }

        // ============================================================
        // EMPLOYEE - UPLOAD DOCUMENT
        // POST: /Documents/UploadEmployee
        // ============================================================

        [Authorize(Roles = "Employee")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadEmployee(
            string documentType,
            string documentName,
            IFormFile file)
        {
            // ------------------------------------------------------------
            // Find logged-in employee
            // ------------------------------------------------------------

            var employee = await _db.Employees
                .FirstOrDefaultAsync(e =>
                    e.UserId == CurrentUserId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
                return NotFound();

            // ------------------------------------------------------------
            // Validate document type
            // ------------------------------------------------------------

            var allowedDocumentTypes = new[]
            {
                "Aadhaar Card",
                "PAN Card",
                "Passport",
                "Driving License",
                "Education Certificate",
                "Experience Letter",
                "Other"
            };

            if (string.IsNullOrWhiteSpace(documentType) ||
                !allowedDocumentTypes.Contains(documentType))
            {
                TempData["Error"] = "Please select a valid document type.";
                return RedirectToAction(nameof(Employee));
            }

            // ------------------------------------------------------------
            // Validate document name
            // ------------------------------------------------------------

            if (string.IsNullOrWhiteSpace(documentName))
            {
                TempData["Error"] = "Document name is required.";
                return RedirectToAction(nameof(Employee));
            }

            documentName = documentName.Trim();

            if (documentName.Length > 200)
            {
                TempData["Error"] = "Document name is too long.";
                return RedirectToAction(nameof(Employee));
            }

            // ------------------------------------------------------------
            // Validate file
            // ------------------------------------------------------------

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a document file.";
                return RedirectToAction(nameof(Employee));
            }

            // Maximum size: 5 MB
            const long maxFileSize = 5 * 1024 * 1024;

            if (file.Length > maxFileSize)
            {
                TempData["Error"] = "The document size cannot exceed 5 MB.";
                return RedirectToAction(nameof(Employee));
            }

            // ------------------------------------------------------------
            // Validate extension
            // ------------------------------------------------------------

            var allowedExtensions = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
                {
                    ".pdf",
                    ".jpg",
                    ".jpeg",
                    ".png"
                };

            var extension =
                Path.GetExtension(file.FileName);

            if (string.IsNullOrWhiteSpace(extension) ||
                !allowedExtensions.Contains(extension))
            {
                TempData["Error"] =
                    "Only PDF, JPG, JPEG and PNG files are allowed.";

                return RedirectToAction(nameof(Employee));
            }

            // ------------------------------------------------------------
            // Create secure storage path
            // ------------------------------------------------------------

            var documentFolder = Path.Combine(
                "App_Data",
                "HRMDocuments",
                $"company-{CurrentCompanyId}",
                $"employee-{employee.Id}");

            var physicalFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                documentFolder);

            Directory.CreateDirectory(physicalFolder);

            // ------------------------------------------------------------
            // Generate safe unique file name
            // ------------------------------------------------------------

            var generatedFileName =
                $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

            var relativeFilePath = Path.Combine(
                documentFolder,
                generatedFileName);

            var physicalFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                relativeFilePath);

            // ------------------------------------------------------------
            // Save physical file
            // ------------------------------------------------------------

            await using (var stream = new FileStream(
                physicalFilePath,
                FileMode.CreateNew))
            {
                await file.CopyToAsync(stream);
            }

            // ------------------------------------------------------------
            // Save document metadata
            // ------------------------------------------------------------

            var document = new Document
            {
                CompanyId = CurrentCompanyId,
                EmployeeId = employee.Id,

                DocumentKind = "Employee",

                DocumentCategory = "Personal",

                DocumentType = documentType,

                DocumentName = documentName,

                FileName = generatedFileName,

                FilePath = relativeFilePath,

                DocumentStatus = "Uploaded",

                IsOnboardingDocument = false,

                RequiresSignature = false,

                UploadedBy = CurrentUserId,

                CreatedAt = DateTime.UtcNow,

                UpdatedAt = DateTime.UtcNow
            };

            _db.Documents.Add(document);

            await _db.SaveChangesAsync();

            TempData["Success"] =
                $"{documentType} uploaded successfully.";

            return RedirectToAction(nameof(Employee));
        }

        [Authorize(Roles = "Employee,HR")]
        [HttpGet]
        public async Task<IActionResult> ViewDocument(int id)
        {
            var document = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.Id == id &&
                    d.CompanyId == CurrentCompanyId);

            if (document == null)
                return NotFound();

            // Employee can access only their own documents.
            if (User.IsInRole("Employee"))
            {
                var employee = await _db.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e =>
                        e.UserId == CurrentUserId &&
                        e.User.CompanyId == CurrentCompanyId &&
                        e.User.Role == UserRole.Employee);

                if (employee == null ||
                    document.EmployeeId != employee.Id)
                {
                    return Forbid();
                }
            }

            if (string.IsNullOrWhiteSpace(document.FilePath))
                return NotFound();

            var physicalFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                document.FilePath);

            if (!System.IO.File.Exists(physicalFilePath))
                return NotFound();

            var extension =
                Path.GetExtension(document.FileName ?? document.FilePath)
                    .ToLowerInvariant();

            var contentType = extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };

            return PhysicalFile(
                physicalFilePath,
                contentType);
        }

        // ============================================================
        // HR DOCUMENTS
        // GET: /Documents/HR
        // ============================================================
        // ============================================================
        // HR DOCUMENTS
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> HR()
        {
            // ------------------------------------------------------------
            // LOAD COMPANY-SCOPED DOCUMENTS
            // ------------------------------------------------------------

            var documents = await _db.Documents
                .AsNoTracking()
                .Include(d => d.Employee)
                    .ThenInclude(e => e!.User)
                .Where(d =>
                    d.CompanyId == CurrentCompanyId)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();


            // ------------------------------------------------------------
            // LOAD COMPANY-SCOPED EMPLOYEES FOR DROPDOWN
            // ------------------------------------------------------------

            var employees = await _db.Employees
                .AsNoTracking()
                .Include(e => e.User)
                .Where(e =>
                    e.User.CompanyId == CurrentCompanyId)
                .OrderBy(e => e.User.FullName)
                .ToListAsync();

            // ------------------------------------------------------------
            // LOAD HR DOCUMENT TEMPLATES
            // ------------------------------------------------------------

            var templates = await _db.Documents
                .AsNoTracking()
                .Where(d =>
                    d.CompanyId == CurrentCompanyId &&
                    d.DocumentKind == "Template")
                .OrderBy(d => d.DocumentType)
                .ThenByDescending(d => d.TemplateVersion)
                .ToListAsync();

            ViewBag.Templates = templates;

            ViewBag.Employees = employees;


            return View(
                "~/Views/Documents/HR.cshtml",
                documents);
        }

        // ============================================================
        // HR - ADD DOCUMENT TEMPLATE
        // ============================================================

        // ============================================================
        // HR - ADD DOCUMENT TEMPLATE
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddTemplate(
            string templateType,
            string templateName,
            string templateCode,
            IFormFile templateFile)
        {
            // ------------------------------------------------------------
            // VALID TEMPLATE TYPE
            // ------------------------------------------------------------

            var allowedTemplateTypes = new[]
            {
                "Offer Letter",
                "Appointment Letter",
                "Salary Slip",
                "Monthly Attendance Summary",
                "Payroll Report",
                "Bank Letter",
                "On-Site Payroll",
                "Other"
            };

            if (string.IsNullOrWhiteSpace(templateType) ||
                !allowedTemplateTypes.Contains(templateType))
            {
                TempData["Error"] =
                    "Please select a valid template type.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // TEMPLATE NAME
            // ------------------------------------------------------------

            templateName = templateName?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(templateName))
            {
                TempData["Error"] =
                    "Template name is required.";

                return RedirectToAction(nameof(HR));
            }

            if (templateName.Length > 200)
            {
                TempData["Error"] =
                    "Template name is too long.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // TEMPLATE CODE
            // ------------------------------------------------------------

            templateCode = templateCode?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(templateCode))
            {
                TempData["Error"] =
                    "Template code is required.";

                return RedirectToAction(nameof(HR));
            }

            if (templateCode.Length > 100)
            {
                TempData["Error"] =
                    "Template code is too long.";

                return RedirectToAction(nameof(HR));
            }

            templateCode =
                templateCode.ToUpperInvariant();


            // ------------------------------------------------------------
            // TEMPLATE FILE
            // ------------------------------------------------------------

            if (templateFile == null ||
                templateFile.Length == 0)
            {
                TempData["Error"] =
                    "Please select a template file.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // MAXIMUM FILE SIZE
            // ------------------------------------------------------------

            const long maxFileSize = 10 * 1024 * 1024;

            if (templateFile.Length > maxFileSize)
            {
                TempData["Error"] =
                    "The template file cannot exceed 10 MB.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // ALLOWED TEMPLATE FILE TYPES
            // ------------------------------------------------------------

            var allowedExtensions =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ".pdf",
                    ".doc",
                    ".docx",
                    ".xls",
                    ".xlsx",
                    ".ppt",
                    ".pptx"
                };


            var extension =
                Path.GetExtension(templateFile.FileName);


            if (string.IsNullOrWhiteSpace(extension) ||
                !allowedExtensions.Contains(extension))
            {
                TempData["Error"] =
                    "Allowed template formats are PDF, DOC, DOCX, XLS, XLSX, PPT and PPTX.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // FIND NEXT VERSION
            // ------------------------------------------------------------

            var existingTemplates = await _db.Documents
                .Where(d =>
                    d.CompanyId == CurrentCompanyId &&
                    d.DocumentKind == "Template" &&
                    d.TemplateCode == templateCode)
                .ToListAsync();


            var nextVersion = existingTemplates
                .Select(d => d.TemplateVersion ?? 0)
                .DefaultIfEmpty(0)
                .Max() + 1;


            // ------------------------------------------------------------
            // ARCHIVE CURRENT ACTIVE TEMPLATE
            // ------------------------------------------------------------

            foreach (var existingTemplate in existingTemplates)
            {
                if (existingTemplate.DocumentStatus == "Active")
                {
                    existingTemplate.DocumentStatus =
                        "Archived";

                    existingTemplate.UpdatedAt =
                        DateTime.UtcNow;
                }
            }


            // ------------------------------------------------------------
            // CREATE TEMPLATE STORAGE FOLDER
            // ------------------------------------------------------------

            var templateFolder = Path.Combine(
                "App_Data",
                "HRMDocuments",
                $"company-{CurrentCompanyId}",
                "templates",
                templateCode);

            var physicalFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                templateFolder);

            Directory.CreateDirectory(
                physicalFolder);


            // ------------------------------------------------------------
            // SAFE FILE NAME
            // ------------------------------------------------------------

            var generatedFileName =
                $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";


            var relativeFilePath = Path.Combine(
                templateFolder,
                generatedFileName);


            var physicalFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                relativeFilePath);


            // ------------------------------------------------------------
            // SAVE ACTUAL TEMPLATE FILE
            // ------------------------------------------------------------

            await using (var stream = new FileStream(
                physicalFilePath,
                FileMode.CreateNew))
            {
                await templateFile.CopyToAsync(stream);
            }


            // ------------------------------------------------------------
            // SAVE TEMPLATE METADATA
            // ------------------------------------------------------------

            var template = new Document
            {
                CompanyId = CurrentCompanyId,

                EmployeeId = null,

                DocumentKind = "Template",
                DocumentCategory = "HR Template",
                DocumentType = templateType,

                DocumentName = templateName,

                TemplateCode = templateCode,
                TemplateVersion = nextVersion,

                TemplateContent = null,

                FileName = generatedFileName,
                FilePath = relativeFilePath,

                DocumentStatus = "Active",

                IsOnboardingDocument = false,
                RequiresSignature = false,

                UploadedBy = CurrentUserId,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };


            _db.Documents.Add(template);

            await _db.SaveChangesAsync();


            TempData["Success"] =
                $"{templateType} template version {nextVersion} uploaded successfully.";


            return RedirectToAction(nameof(HR));
        }

        // ============================================================
        // HR - OPEN TEMPLATE
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> OpenTemplate(int id)
        {
            var template = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.Id == id &&
                    d.CompanyId == CurrentCompanyId &&
                    d.DocumentKind == "Template");

            if (template == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(template.FilePath))
                return NotFound();

            var physicalFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                template.FilePath);

            if (!System.IO.File.Exists(physicalFilePath))
                return NotFound();

            var extension =
                Path.GetExtension(
                    template.FileName ?? template.FilePath)
                    .ToLowerInvariant();

            var contentType = extension switch
            {
                ".pdf" =>
                    "application/pdf",

                ".doc" =>
                    "application/msword",

                ".docx" =>
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",

                ".xls" =>
                    "application/vnd.ms-excel",

                ".xlsx" =>
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",

                ".ppt" =>
                    "application/vnd.ms-powerpoint",

                ".pptx" =>
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",

                _ =>
                    "application/octet-stream"
            };

            var displayFileName =
                Path.GetFileName(
                    template.FileName ?? "template");

            displayFileName =
                displayFileName.Replace("\"", "");

            Response.Headers["Content-Disposition"] =
                $"inline; filename=\"{displayFileName}\"";

            return PhysicalFile(
                physicalFilePath,
                contentType);
        }

        // ============================================================
        // HR - DOWNLOAD TEMPLATE
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpGet]
        public async Task<IActionResult> DownloadTemplate(int id)
        {
            var template = await _db.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(d =>
                    d.Id == id &&
                    d.CompanyId == CurrentCompanyId &&
                    d.DocumentKind == "Template");

            if (template == null)
                return NotFound();

            if (string.IsNullOrWhiteSpace(template.FilePath))
                return NotFound();

            var physicalFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                template.FilePath);

            if (!System.IO.File.Exists(physicalFilePath))
                return NotFound();

            var extension =
                Path.GetExtension(
                    template.FileName ?? template.FilePath)
                    .ToLowerInvariant();

            var contentType = extension switch
            {
                ".pdf" =>
                    "application/pdf",

                ".doc" =>
                    "application/msword",

                ".docx" =>
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document",

                ".xls" =>
                    "application/vnd.ms-excel",

                ".xlsx" =>
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",

                ".ppt" =>
                    "application/vnd.ms-powerpoint",

                ".pptx" =>
                    "application/vnd.openxmlformats-officedocument.presentationml.presentation",

                _ =>
                    "application/octet-stream"
            };

            var downloadFileName =
                Path.GetFileName(
                    template.FileName ?? "template");

            return PhysicalFile(
                physicalFilePath,
                contentType,
                downloadFileName);
        }

        // ============================================================
        // HR - UPLOAD DOCUMENT FOR EMPLOYEE
        // ============================================================

        [Authorize(Roles = "HR")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadHR(
            int employeeId,
            string documentType,
            string documentName,
            IFormFile file)
        {
            // ------------------------------------------------------------
            // EMPLOYEE MUST BELONG TO CURRENT COMPANY
            // ------------------------------------------------------------

            var employee = await _db.Employees
                .FirstOrDefaultAsync(e =>
                    e.Id == employeeId &&
                    e.User.CompanyId == CurrentCompanyId &&
                    e.User.Role == UserRole.Employee);

            if (employee == null)
            {
                TempData["Error"] =
                    "Invalid employee selected.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // VALID DOCUMENT TYPE
            // ------------------------------------------------------------

            var allowedDocumentTypes = new[]
            {
                "Aadhaar Card",
                "PAN Card",
                "Passport",
                "Driving License",
                "Education Certificate",
                "Experience Letter",
                "Offer Letter",
                "Appointment Letter",
                "Other"
            };

            if (string.IsNullOrWhiteSpace(documentType) ||
                !allowedDocumentTypes.Contains(documentType))
            {
                TempData["Error"] =
                    "Please select a valid document type.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // DOCUMENT NAME
            // ------------------------------------------------------------

            if (string.IsNullOrWhiteSpace(documentName))
            {
                TempData["Error"] =
                    "Document name is required.";

                return RedirectToAction(nameof(HR));
            }

            documentName = documentName.Trim();

            if (documentName.Length > 200)
            {
                TempData["Error"] =
                    "Document name is too long.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // FILE VALIDATION
            // ------------------------------------------------------------

            if (file == null || file.Length == 0)
            {
                TempData["Error"] =
                    "Please select a document file.";

                return RedirectToAction(nameof(HR));
            }

            const long maxFileSize = 5 * 1024 * 1024;

            if (file.Length > maxFileSize)
            {
                TempData["Error"] =
                    "The document size cannot exceed 5 MB.";

                return RedirectToAction(nameof(HR));
            }


            var allowedExtensions = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
                {
                    ".pdf",
                    ".jpg",
                    ".jpeg",
                    ".png"
                };

            var extension =
                Path.GetExtension(file.FileName);

            if (string.IsNullOrWhiteSpace(extension) ||
                !allowedExtensions.Contains(extension))
            {
                TempData["Error"] =
                    "Only PDF, JPG, JPEG and PNG files are allowed.";

                return RedirectToAction(nameof(HR));
            }


            // ------------------------------------------------------------
            // STORAGE LOCATION
            // ------------------------------------------------------------

            var documentFolder = Path.Combine(
                "App_Data",
                "HRMDocuments",
                $"company-{CurrentCompanyId}",
                $"employee-{employee.Id}");

            var physicalFolder = Path.Combine(
                Directory.GetCurrentDirectory(),
                documentFolder);

            Directory.CreateDirectory(physicalFolder);


            // ------------------------------------------------------------
            // GENERATE SAFE FILE NAME
            // ------------------------------------------------------------

            var generatedFileName =
                $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

            var relativeFilePath = Path.Combine(
                documentFolder,
                generatedFileName);

            var physicalFilePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                relativeFilePath);


            // ------------------------------------------------------------
            // SAVE FILE
            // ------------------------------------------------------------

            await using (var stream = new FileStream(
                physicalFilePath,
                FileMode.CreateNew))
            {
                await file.CopyToAsync(stream);
            }


            // ------------------------------------------------------------
            // SAVE DOCUMENT METADATA
            // ------------------------------------------------------------

            var document = new Document
            {
                CompanyId = CurrentCompanyId,
                EmployeeId = employee.Id,

                DocumentKind = "Employee",
                DocumentCategory = "HR",
                DocumentType = documentType,
                DocumentName = documentName,

                FileName = generatedFileName,
                FilePath = relativeFilePath,

                DocumentStatus = "Uploaded",

                IsOnboardingDocument = false,
                RequiresSignature = false,

                UploadedBy = CurrentUserId,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Documents.Add(document);

            await _db.SaveChangesAsync();


            TempData["Success"] =
                $"{documentType} uploaded successfully for {employee.User.FullName}.";

            return RedirectToAction(nameof(HR));
        }
    }
}