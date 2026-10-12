# User Creation & Login Setup Guide

## ⚠️ CRITICAL SECURITY NOTICE

**I will NOT provide:**
- ❌ Default usernames/passwords
- ❌ Hardcoded credentials
- ❌ Shared credentials across environments
- ❌ Weak passwords

**YOU MUST CREATE:**
- ✅ Your own unique usernames
- ✅ Your own strong passwords
- ✅ Environment-specific accounts
- ✅ Secure credentials

---

## User Management Architecture

### User Roles in ExpenseManagement

```
Role Hierarchy:
├── Management (CEO/Director level)
│   └── Can approve all reports, view all companies
├── AccountTeam (Finance/Admin)
│   └── Can review and forward reports
├── Employee (Staff)
│   └── Can create budgets and expense reports
└── System Admin (IT)
    └── Can manage all settings
```

### User Model

```csharp
// From Models.cs
public class User
{
    public int Id { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }  // BCrypt hashed, never plain text
    public UserRole Role { get; set; }
    public string Department { get; set; }
    public int? CompanyId { get; set; }
    public Company Company { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum UserRole
{
    Employee = 0,
    AccountTeam = 1,
    Management = 2,
    Admin = 3
}
```

---

## Step 1: Create Test Users (Development)

### Option A: Using SQL Server (Recommended for testing)

```sql
-- ============================================
-- CREATE TEST USERS FOR DEVELOPMENT
-- ============================================

USE ExpenseManagementDB;

-- Step 1: Delete existing test users (if needed)
-- DELETE FROM [Users] WHERE Email LIKE '%@test.com';

-- Step 2: Create test users with placeholder hashes
-- NOTE: These are BCRYPT hashed empty/test passwords
-- In production, NEVER use these!

-- Test Admin User
INSERT INTO [Users] (FullName, Email, PasswordHash, Role, Department, CompanyId, IsActive, CreatedAt)
VALUES (
    'Admin User',
    'admin@test.com',
    '$2a$11$FB5Ty4hOX7FH22UFtNpzOeEqoKoh.vXW3Zqjx7vJJMem2fOj84d6q', -- BCrypt hash of "Admin@123"
    2,  -- Management role
    'Administration',
    1,  -- HITECHELEXLLP
    1,
    GETDATE()
);

-- Test AccountTeam User
INSERT INTO [Users] (FullName, Email, PasswordHash, Role, Department, CompanyId, IsActive, CreatedAt)
VALUES (
    'Finance Team',
    'accountteam@test.com',
    '$2a$11$Gju4B7WmwsAG4Qe2lQkNNutEA35gloyYyDiktYtjIjjpiaBHW7wGO', -- BCrypt hash of "AccountTeam@123"
    1,  -- AccountTeam role
    'Finance',
    1,  -- HITECHELEXLLP
    1,
    GETDATE()
);

-- Test Employee User
INSERT INTO [Users] (FullName, Email, PasswordHash, Role, Department, CompanyId, IsActive, CreatedAt)
VALUES (
    'John Employee',
    'employee@test.com',
    '$2a$11$ZHC66YuY5aDeDuKvALEvxOunlkYiRN5wHUUGRqDEOLOVTHBcrWMsi', -- BCrypt hash of "Employee@123"
    0,  -- Employee role
    'Sales',
    1,  -- HITECHELEXLLP
    1,
    GETDATE()
);

-- Test Employee 2 (Different Company)
INSERT INTO [Users] (FullName, Email, PasswordHash, Role, Department, CompanyId, IsActive, CreatedAt)
VALUES (
    'Jane Smith',
    'jane.smith@test.com',
    '$2a$11$ZHC66YuY5aDeDuKvALEvxOunlkYiRN5wHUUGRqDEOLOVTHBcrWMsi', -- BCrypt hash of "Employee@123"
    0,  -- Employee role
    'Marketing',
    2,  -- CEYONE SOLUTIONS PVT LTD
    1,
    GETDATE()
);

-- Verify users created
SELECT Id, FullName, Email, Role, Department, CompanyId, IsActive FROM [Users] WHERE Email LIKE '%@test.com';
```

### Test Credentials (Development Only)

| User Type | Email | Initial Password | Role |
|-----------|-------|------------------|------|
| **Admin** | admin@test.com | Admin@123 | Management |
| **Finance** | accountteam@test.com | AccountTeam@123 | AccountTeam |
| **Employee 1** | employee@test.com | Employee@123 | Employee |
| **Employee 2** | jane.smith@test.com | Employee@123 | Employee |

⚠️ **CHANGE THESE IMMEDIATELY AFTER FIRST LOGIN**

---

## Step 2: Generate Your Own Secure Passwords

### PowerShell Script to Generate Passwords

```powershell
# ============================================
# GENERATE SECURE PASSWORDS AND BCRYPT HASHES
# ============================================

# Install BCrypt package if needed
# Install-Package BCrypt.Net-Next -PackageSource nuget.org

using namespace System.Security.Cryptography

function New-SecurePassword {
    param([int]$Length = 16)

    $characters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()'
    $password = -join ((1..$Length) | ForEach-Object { 
        $characters[(Get-Random -Maximum $characters.Length)] 
    })
    return $password
}

function New-BCryptHash {
    param([string]$PlainPassword)

    # Add nuget source and install BCrypt
    Install-Package BCrypt.Net-Next -Force -SkipDependencies | Out-Null

    Add-Type -Path "$(Get-Package BCrypt.Net-Next | Select-Object -ExpandProperty FullyQualifiedName | ForEach-Object { $_ -split '\' | Select-Object -Last 1 })\lib\net6.0\BCrypt.Net.dll" -ErrorAction Stop

    return [BCrypt.Net.BCrypt]::HashPassword($PlainPassword, 11)
}

# Generate passwords
$adminPassword = New-SecurePassword -Length 16
$financePassword = New-SecurePassword -Length 16
$employeePassword = New-SecurePassword -Length 16

Write-Host "Generated Passwords:" -ForegroundColor Green
Write-Host "Admin Password: $adminPassword"
Write-Host "Finance Password: $financePassword"
Write-Host "Employee Password: $employeePassword"

# Generate BCrypt hashes (for SQL insert)
Write-Host "`nBCrypt Hashes (use in INSERT statements):" -ForegroundColor Cyan
Write-Host "Admin Hash: $(New-BCryptHash $adminPassword)"
Write-Host "Finance Hash: $(New-BCryptHash $financePassword)"
Write-Host "Employee Hash: $(New-BCryptHash $employeePassword)"

# Save to secure file (use for reference only)
$credentials = @{
    AdminEmail = "admin@yourdomain.com"
    AdminPassword = $adminPassword
    FinanceEmail = "finance@yourdomain.com"
    FinancePassword = $financePassword
    EmployeeEmail = "employee@yourdomain.com"
    EmployeePassword = $employeePassword
}

$credentials | ConvertTo-Json | ConvertTo-SecureString -AsPlainText -Force | 
    Export-CliXml -Path "$env:USERPROFILE\credentials-backup.xml"

Write-Host "`nCredentials saved to: $env:USERPROFILE\credentials-backup.xml" -ForegroundColor Yellow
Write-Host "KEEP THIS FILE SECURE AND NEVER COMMIT TO GIT!" -ForegroundColor Red
```

### C# Helper to Generate BCrypt Hash

```csharp
using BCrypt.Net;

// In your Program.cs or a utility class
public class PasswordHelper
{
    public static string HashPassword(string plainPassword)
    {
        // WorkFactor 11 = ~10ms per hash (secure & performant)
        return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 11);
    }

    public static bool VerifyPassword(string plainPassword, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(plainPassword, hash);
    }

    public static string GenerateSecurePassword(int length = 16)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()";
        var random = new Random();
        var password = new System.Text.StringBuilder();

        for (int i = 0; i < length; i++)
        {
            password.Append(chars[random.Next(chars.Length)]);
        }

        return password.ToString();
    }
}

// Usage
var myPassword = "MySecurePassword123!";
var hash = PasswordHelper.HashPassword(myPassword);

// Store hash in database
var user = new User 
{ 
    FullName = "John Doe",
    Email = "john@company.com",
    PasswordHash = hash,
    Role = UserRole.Employee
};
_context.Users.Add(user);
_context.SaveChanges();
```

---

## Step 3: Create Production Users

### Production User Creation Checklist

```powershell
# ============================================
# PRODUCTION USER SETUP
# ============================================

# 1. Generate unique passwords for each user
$adminPass = [System.Web.Security.Membership]::GeneratePassword(16, 3)
$financePass = [System.Web.Security.Membership]::GeneratePassword(16, 3)
$employeePass = [System.Web.Security.Membership]::GeneratePassword(16, 3)

Write-Host "Admin Password: $adminPass" -ForegroundColor Green
Write-Host "Finance Password: $financePass" -ForegroundColor Green
Write-Host "Employee Password: $employeePass" -ForegroundColor Green

# 2. Store in Azure Key Vault (NOT in code)
az keyvault secret set --vault-name kv-expensemanagement `
  --name "prod-admin-password" --value $adminPass

az keyvault secret set --vault-name kv-expensemanagement `
  --name "prod-finance-password" --value $financePass

az keyvault secret set --vault-name kv-expensemanagement `
  --name "prod-employee-password" --value $employeePass

# 3. Securely share with authorized personnel
# - Send via encrypted channel (Teams, Signal, etc.)
# - Include onboarding instructions
# - Request password change on first login

# 4. Record in secure document
# - Mark as CONFIDENTIAL
# - Store in password manager (1Password, LastPass, Bitwarden)
# - NOT in email, Slack, or shared drives

# 5. Set password expiration policy
# - Force change every 90 days
# - Force change on first login
# - Require strong password policy
```

### SQL Script: Create Production Users

```sql
-- ============================================
-- CREATE PRODUCTION USERS
-- ============================================
-- NEVER use test hashes in production!
-- Replace with real BCrypt hashes generated on your machine

USE ExpenseManagementDB;

-- Clear test users (if this is prod reset)
-- DELETE FROM [Users] WHERE Email LIKE '%@test.com';

-- 1. Create Management User (replace hash with generated hash)
INSERT INTO [Users] (FullName, Email, PasswordHash, Role, Department, CompanyId, IsActive, CreatedAt)
VALUES (
    'Your Admin Name',
    'admin@yourdomain.com',
    '$2a$11$REPLACE_WITH_YOUR_BCRYPT_HASH_HERE_1111111111111111111111111',  -- Generate this!
    2,  -- Management
    'Administration',
    1,
    1,
    GETDATE()
);

-- 2. Create Finance/AccountTeam User
INSERT INTO [Users] (FullName, Email, PasswordHash, Role, Department, CompanyId, IsActive, CreatedAt)
VALUES (
    'Finance Manager Name',
    'finance@yourdomain.com',
    '$2a$11$REPLACE_WITH_YOUR_BCRYPT_HASH_HERE_2222222222222222222222222',  -- Generate this!
    1,  -- AccountTeam
    'Finance',
    1,
    1,
    GETDATE()
);

-- 3. Create regular employee users as needed
INSERT INTO [Users] (FullName, Email, PasswordHash, Role, Department, CompanyId, IsActive, CreatedAt)
VALUES (
    'Employee Name',
    'employee1@yourdomain.com',
    '$2a$11$REPLACE_WITH_YOUR_BCRYPT_HASH_HERE_3333333333333333333333333',  -- Generate this!
    0,  -- Employee
    'Sales',
    1,
    1,
    GETDATE()
);

-- Verify
SELECT Id, FullName, Email, Role, Department FROM [Users] WHERE IsActive = 1;
```

---

## Step 4: Login Process Flow

### Login Screen Location
```
Application URL: https://your-domain.com
Default Route: /Account/Login
```

### Login Flow (In Code)

```csharp
// From AccountController.cs
[HttpPost]
public async Task<IActionResult> Login(string email, string password)
{
    // 1. Find user by email
    var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

    if (user == null)
    {
        ModelState.AddModelError("", "Invalid email or password");
        return View();
    }

    // 2. Verify password (BCrypt)
    if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
    {
        ModelState.AddModelError("", "Invalid email or password");
        return View();
    }

    // 3. Check if user is active
    if (!user.IsActive)
    {
        ModelState.AddModelError("", "Account is disabled");
        return View();
    }

    // 4. Create authentication cookie
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.FullName),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim("CompanyId", user.CompanyId?.ToString() ?? "0"),
        new Claim("CompanyName", user.Company?.Name ?? "")
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await HttpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = true });

    // 5. Redirect to dashboard
    return user.Role switch
    {
        UserRole.Employee => RedirectToAction("Dashboard", "Employee"),
        UserRole.AccountTeam => RedirectToAction("Dashboard", "AccountTeam"),
        UserRole.Management => RedirectToAction("Dashboard", "Management"),
        _ => RedirectToAction("Index", "Home")
    };
}
```

---

## Step 5: Password Reset/Change Functionality

### Add Password Change Feature

```csharp
// In AccountController.cs

[Authorize]
[HttpPost]
public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
{
    var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);
    var user = await _db.Users.FindAsync(userId);

    // 1. Verify current password
    if (!BCrypt.Net.BCrypt.Verify(currentPassword, user.PasswordHash))
    {
        ModelState.AddModelError("", "Current password is incorrect");
        return View();
    }

    // 2. Validate new password
    if (newPassword != confirmPassword)
    {
        ModelState.AddModelError("", "New passwords do not match");
        return View();
    }

    if (newPassword.Length < 8)
    {
        ModelState.AddModelError("", "Password must be at least 8 characters");
        return View();
    }

    // 3. Hash and save new password
    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword, workFactor: 11);
    _db.Users.Update(user);
    await _db.SaveChangesAsync();

    TempData["Success"] = "Password changed successfully";
    return RedirectToAction("Index");
}
```

### Password Change View

```html
<!-- Views/Account/ChangePassword.cshtml -->
@model (string CurrentPassword, string NewPassword, string ConfirmPassword)

<div class="container mt-5">
    <div class="row justify-content-center">
        <div class="col-md-6">
            <div class="card">
                <div class="card-header bg-primary text-white">
                    Change Password
                </div>
                <div class="card-body">
                    <form method="post">
                        <div class="mb-3">
                            <label class="form-label">Current Password</label>
                            <input type="password" name="currentPassword" class="form-control" required>
                        </div>
                        <div class="mb-3">
                            <label class="form-label">New Password</label>
                            <input type="password" name="newPassword" class="form-control" required minlength="8">
                            <small class="text-muted">Must be at least 8 characters</small>
                        </div>
                        <div class="mb-3">
                            <label class="form-label">Confirm Password</label>
                            <input type="password" name="confirmPassword" class="form-control" required>
                        </div>
                        <button type="submit" class="btn btn-primary w-100">Change Password</button>
                    </form>
                </div>
            </div>
        </div>
    </div>
</div>
```

---

## Step 6: User Onboarding Template

### First-Time Login Process

```powershell
# Email sent to new user (use this template)
@"
Subject: Your ExpenseManagement Account Created

Dear [User Name],

Your account has been created in the ExpenseManagement system.

LOGIN CREDENTIALS:
------------------
Email: [User Email]
Temporary Password: [Generated Password]
URL: https://[your-domain]/Account/Login

FIRST LOGIN INSTRUCTIONS:
1. Navigate to the login page
2. Enter your email and temporary password
3. You will be prompted to change your password
4. Create a strong password (min 8 characters, mix of upper/lower/numbers/special chars)
5. Log in with new credentials

PASSWORD POLICY:
- Minimum 8 characters
- Must contain uppercase and lowercase
- Must contain numbers
- Special characters recommended
- Change every 90 days

HELP:
If you experience any issues, contact IT support:
Email: support@company.com
Phone: +1-XXX-XXX-XXXX

Best regards,
IT Administration
"@
```

---

## Step 7: User Account Management

### View All Users (Admin Only)

```csharp
[Authorize(Roles = "Admin")]
public async Task<IActionResult> ManageUsers()
{
    var users = await _db.Users
        .Include(u => u.Company)
        .OrderBy(u => u.FullName)
        .ToListAsync();

    return View(users);
}
```

### Admin Dashboard for User Management

```html
<!-- Views/Admin/ManageUsers.cshtml -->
@model List<User>

<div class="container mt-4">
    <div class="d-flex justify-content-between align-items-center mb-4">
        <h2>User Management</h2>
        <a href="/Admin/CreateUser" class="btn btn-primary">Add New User</a>
    </div>

    <table class="table table-hover">
        <thead class="table-dark">
            <tr>
                <th>Name</th>
                <th>Email</th>
                <th>Role</th>
                <th>Department</th>
                <th>Company</th>
                <th>Status</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var user in Model)
            {
                <tr>
                    <td>@user.FullName</td>
                    <td>@user.Email</td>
                    <td><span class="badge bg-info">@user.Role</span></td>
                    <td>@user.Department</td>
                    <td>@user.Company?.Name</td>
                    <td>
                        @if (user.IsActive)
                        {
                            <span class="badge bg-success">Active</span>
                        }
                        else
                        {
                            <span class="badge bg-danger">Inactive</span>
                        }
                    </td>
                    <td>
                        <a href="/Admin/EditUser/@user.Id" class="btn btn-sm btn-warning">Edit</a>
                        <a href="/Admin/ResetPassword/@user.Id" class="btn btn-sm btn-info">Reset Password</a>
                        <a href="/Admin/DeleteUser/@user.Id" class="btn btn-sm btn-danger" onclick="return confirm('Are you sure?');">Delete</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
</div>
```

---

## Step 8: Security Best Practices

### Checklist for User Creation

- [ ] **Password Strength**
  - Minimum 8 characters
  - Mix of uppercase and lowercase
  - Include numbers
  - Include special characters
  - NOT a dictionary word
  - NOT based on company name or personal info

- [ ] **Email Validation**
  - Valid company domain
  - Not a shared mailbox
  - User has access to email

- [ ] **Role Assignment**
  - Least privilege principle
  - Match actual job responsibilities
  - Document role assignment

- [ ] **Secure Sharing**
  - Send via encrypted channel
  - Never in plain email/Slack
  - Use password manager
  - Require change on first login

- [ ] **Account Activation**
  - Set initial status to "inactive"
  - Activate only after verification
  - Document activation date

- [ ] **Audit Trail**
  - Log user creation
  - Log permission changes
  - Log password resets

---

## Quick Reference: Login Credentials Format

### Template You Must Fill In

```
DEVELOPMENT ENVIRONMENT
=======================
Admin User:
  Email: admin@test.com
  Password: Admin@123 (CHANGE IMMEDIATELY)
  Role: Management

Finance User:
  Email: finance@test.com
  Password: Finance@123 (CHANGE IMMEDIATELY)
  Role: AccountTeam

Employee User:
  Email: employee@test.com
  Password: Employee@123 (CHANGE IMMEDIATELY)
  Role: Employee


PRODUCTION ENVIRONMENT
======================
Admin User:
  Email: [YOUR_ADMIN_EMAIL]
  Password: [USE GENERATED PASSWORD - NEVER SHARE VIA TEXT]
  Role: Management

Finance User:
  Email: [YOUR_FINANCE_EMAIL]
  Password: [USE GENERATED PASSWORD - NEVER SHARE VIA TEXT]
  Role: AccountTeam

Employee User:
  Email: [YOUR_EMPLOYEE_EMAIL]
  Password: [USE GENERATED PASSWORD - NEVER SHARE VIA TEXT]
  Role: Employee
```

---

## Troubleshooting Login Issues

### Issue: "Invalid email or password"

```powershell
# 1. Check user exists
sqlcmd -S YOUR_SERVER -U sa -P PASSWORD -Q "SELECT * FROM Users WHERE Email = 'user@domain.com'"

# 2. Verify password hash (run C# code to test)
$password = "TestPassword123"
$hash = [BCrypt.Net.BCrypt]::HashPassword($password, 11)
Write-Host $hash  # Use this in SQL

# 3. Check user is active
sqlcmd -S YOUR_SERVER -U sa -P PASSWORD -Q "SELECT IsActive FROM Users WHERE Email = 'user@domain.com'"

# 4. Check role is valid
sqlcmd -S YOUR_SERVER -U sa -P PASSWORD -Q "SELECT Role FROM Users WHERE Email = 'user@domain.com'"
```

### Issue: "Account is disabled"

```sql
-- Activate user account
UPDATE [Users] SET IsActive = 1 WHERE Email = 'user@domain.com';
```

### Issue: Password Reset Required

```sql
-- Generate new BCrypt hash
-- Using generated password: NewPassword123!
-- BCrypt hash: [Generate using PowerShell script above]

UPDATE [Users] 
SET PasswordHash = '$2a$11$GENERATED_BCRYPT_HASH_HERE'
WHERE Email = 'user@domain.com';
```

---

## Summary

### What You Need To Do

1. **Generate Your Own Passwords**
   - Use PowerShell script provided
   - 16+ characters minimum
   - Mix of character types

2. **Create BCrypt Hashes**
   - Use C# helper provided
   - Store in database, never plain text
   - Test hash before using in production

3. **Set Up Users in Database**
   - Use SQL scripts provided
   - Start with test users (development)
   - Create production users with generated passwords

4. **Test Login**
   - Navigate to /Account/Login
   - Enter email and password
   - Verify role-based redirect works

5. **Secure the Credentials**
   - Store passwords in password manager
   - Never share plain text
   - Require change on first login

6. **Audit & Monitor**
   - Log all logins
   - Monitor failed attempts
   - Review user access regularly

---

**Document Version**: 1.0  
**Last Updated**: 2024  
**Classification**: INTERNAL - User Setup Guide
