# 💼 ExpenseTrack — ASP.NET MVC Expense Management System

A full-featured, role-based expense management web application built with ASP.NET Core MVC, Entity Framework Core, and Bootstrap 5.

---

## 🏗️ Project Structure

```
ExpenseManagement/
├── Controllers/
│   ├── AccountController.cs       ← Login / Register / Logout
│   ├── EmployeeController.cs      ← Employee features
│   ├── AccountTeamController.cs   ← Account team features
│   └── ManagementController.cs    ← Management features
├── Models/
│   └── Models.cs                  ← User, Budget, ExpenseClaim, ExpenseReport
├── ViewModels/
│   └── ViewModels.cs              ← All form/display view models
├── Data/
│   └── AppDbContext.cs            ← EF Core DbContext + seed data
├── Views/
│   ├── Account/    (Login, Register)
│   ├── Employee/   (Dashboard, MyBudgets, SubmitExpense, MyClaims, GenerateReport, MyReports)
│   ├── AccountTeam/(Dashboard, AllocateBudget, Budgets, Claims, Reports, Employees)
│   ├── Management/ (Dashboard, Reports, Overview, BudgetOverview, AllClaims)
│   └── Shared/     (_Layout.cshtml)
├── Migrations/
├── appsettings.json
└── Program.cs
```

---

## 👥 Three Login Roles

| Role | Email | Password |
|------|-------|----------|
| **Management** | management@company.com | Management@123 |
| **Account Team** | accounts@company.com | Accounts@123 |
| **Employee** | employee@company.com | Employee@123 |

---

## 🔄 Complete Workflow

```
1. ACCOUNT TEAM → Allocates budget to employee
         ↓
2. EMPLOYEE → Submits expense claims (bills)
         ↓
3. ACCOUNT TEAM → Approves or Declines each claim
         ↓
4. EMPLOYEE → When done, generates final Expense Report
         ↓
5. ACCOUNT TEAM → Verifies report, forwards to Management
         ↓
6. MANAGEMENT → Reviews and closes the report
```

---

## ✨ Features by Role

### 👷 Employee
- View allocated budgets with live balance tracker
- Submit expense claims with:
  - Title, Description, Category, Amount
  - Receipt upload (JPG/PNG/PDF)
  - Budget selector showing available balance
- Real-time balance deduction on submission
- Balance restored if claim is declined
- View all claims with Pending/Approved/Declined filter
- Generate final expense report when budget is exhausted
- Track report status through full pipeline

### 🧾 Account Team
- Allocate budgets to employees with purpose & validity date
- View all employee budgets with utilization charts
- Review pending expense claims:
  - Approve or Decline with remarks
  - View attached receipts
- Verify expense reports submitted by employees
- Forward verified reports to Management
- View all employees and their spending

### 📊 Management
- Dashboard with company-wide KPIs
- Overall budget utilization bar
- Review expense reports forwarded by Account Team
- Close/finalize reports with management notes
- Full employee expense overview
- Budget overview across all employees
- View all claims across the organization

---

## 🛠️ Setup Instructions

### Prerequisites
- .NET 8 SDK
- SQL Server (LocalDB is fine for dev)
- Visual Studio 2022 or VS Code

### Steps

1. **Clone / extract** the project

2. **Update connection string** in `appsettings.json`:
   ```json
   "ConnectionStrings": {
     "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=ExpenseManagementDB;Trusted_Connection=True"
   }
   ```

3. **Run migrations** (Database is auto-migrated on startup, or manually):
   ```bash
   dotnet ef database update
   ```

4. **Run the application**:
   ```bash
   dotnet run
   ```

5. **Open browser** at `https://localhost:5001` (or the port shown in terminal)

6. **Login** with one of the demo credentials above

---

## 🗄️ Database Schema

### Users
| Column | Type | Description |
|--------|------|-------------|
| Id | int PK | Auto increment |
| FullName | nvarchar(100) | User's full name |
| Email | nvarchar(150) | Unique email |
| PasswordHash | nvarchar | BCrypt hashed |
| Role | int | 0=Employee, 1=AccountTeam, 2=Management |
| Department | nvarchar | Optional dept |

### Budgets
| Column | Type | Description |
|--------|------|-------------|
| Id | int PK | Auto increment |
| EmployeeId | int FK | → Users |
| AllocatedById | int FK | → Users (account team) |
| TotalAmount | decimal(18,2) | Allocated amount |
| SpentAmount | decimal(18,2) | Auto-tracked spent |
| Purpose | nvarchar | Budget purpose/project |
| ValidUntil | datetime? | Optional expiry |
| IsActive | bit | Active/Closed flag |

### ExpenseClaims
| Column | Type | Description |
|--------|------|-------------|
| Id | int PK | Auto increment |
| EmployeeId | int FK | → Users |
| BudgetId | int FK | → Budgets |
| Title | nvarchar(200) | Claim title |
| Category | nvarchar(100) | Travel, Food, etc. |
| Amount | decimal(18,2) | Claimed amount |
| ReceiptPath | nvarchar? | File path |
| Status | int | 0=Pending, 1=Approved, 2=Declined |
| AccountTeamRemarks | nvarchar? | Reviewer notes |
| ExpenseReportId | int? FK | Linked report |

### ExpenseReports
| Column | Type | Description |
|--------|------|-------------|
| Id | int PK | Auto increment |
| Status | int | Draft→SubmittedToAccountTeam→ForwardedToManagement→Closed |
| TotalClaimed | decimal | Sum of claimed |
| TotalApproved | decimal | Sum of approved |
| AccountTeamNotes | nvarchar? | AT verification notes |
| ManagementNotes | nvarchar? | Mgmt closing notes |

---

## 🎨 Tech Stack
- **ASP.NET Core MVC 8** — Framework
- **Entity Framework Core 8** — ORM
- **SQL Server / LocalDB** — Database
- **Cookie Authentication** — Auth
- **BCrypt.Net** — Password hashing
- **Bootstrap 5.3** — UI
- **Font Awesome 6.5** — Icons

---

## 📝 Notes
- Passwords are BCrypt hashed (never stored plaintext)
- Receipts stored in `wwwroot/uploads/receipts/`
- Budget balance auto-updates on claim submission/decline
- Role-based authorization on all controllers
- Responsive sidebar layout
