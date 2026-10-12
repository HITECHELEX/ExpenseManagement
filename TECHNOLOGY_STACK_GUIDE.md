# ExpenseManagement - Technology Stack Documentation

## Table of Contents
1. [Overview](#overview)
2. [Technology Architecture](#technology-architecture)
3. [Backend Technologies](#backend-technologies)
4. [Frontend Technologies](#frontend-technologies)
5. [Database Technologies](#database-technologies)
6. [Authentication & Security](#authentication--security)
7. [Data Processing & Export](#data-processing--export)
8. [Deployment Technologies](#deployment-technologies)
9. [Third-Party Dependencies](#third-party-dependencies)
10. [Integration Points](#integration-points)
11. [Version Compatibility Matrix](#version-compatibility-matrix)
12. [Technology Decision Rationale](#technology-decision-rationale)
13. [Deployment Tech Impact](#deployment-tech-impact)

---

## Overview

**ExpenseManagement** is a modern, cloud-ready expense management system built with:
- **Backend**: ASP.NET Core 8 (Razor Pages + MVC)
- **Frontend**: HTML5, CSS3, JavaScript, Bootstrap 5
- **Database**: SQL Server 2019+
- **Container**: Docker with multi-stage builds
- **Cloud**: Azure-compatible with AWS support

### System Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    Web Browsers (Users)                      │
└──────────────┬──────────────────────────────────────────────┘
               │ HTTPS
┌──────────────▼──────────────────────────────────────────────┐
│         Load Balancer / Reverse Proxy (Nginx/IIS)           │
└──────────────┬──────────────────────────────────────────────┘
               │ HTTP/1.1
┌──────────────▼──────────────────────────────────────────────┐
│          ASP.NET Core 8 Application (Kestrel)               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  Razor Pages / MVC Controllers                      │    │
│  │  • Razor Views (.cshtml)                           │    │
│  │  • Page Models & Controllers                       │    │
│  │  • Dependency Injection                            │    │
│  └─────────────────────────────────────────────────────┘    │
└──────────────┬──────────────────────────────────────────────┘
               │ ADO.NET
┌──────────────▼──────────────────────────────────────────────┐
│    Entity Framework Core 8 (ORM Layer)                      │
│  • DbContext (AppDbContext)                                 │
│  • Code-First Migrations                                    │
│  • LINQ to SQL                                              │
└──────────────┬──────────────────────────────────────────────┘
               │ SQL
┌──────────────▼──────────────────────────────────────────────┐
│         SQL Server 2019+ (Relational Database)              │
│  • Users, Budgets, ExpenseReports, WalletTransactions       │
│  • Multi-company data isolation                             │
│  • Backup & recovery                                        │
└─────────────────────────────────────────────────────────────┘
```

---

## Technology Architecture

### Layered Architecture

```
┌──────────────────────────────────────────┐
│      Presentation Layer                  │
│  (Razor Pages, Bootstrap, JavaScript)    │
└─────────────────┬────────────────────────┘
                  │
┌─────────────────▼────────────────────────┐
│   Application/API Layer                  │
│  (Controllers, Page Models, Services)    │
└─────────────────┬────────────────────────┘
                  │
┌─────────────────▼────────────────────────┐
│      Business Logic Layer                │
│  (Services, Validators, Calculations)    │
└─────────────────┬────────────────────────┘
                  │
┌─────────────────▼────────────────────────┐
│      Data Access Layer (EF Core)         │
│  (DbContext, Repositories, Migrations)   │
└─────────────────┬────────────────────────┘
                  │
┌─────────────────▼────────────────────────┐
│       Database Layer (SQL Server)        │
│  (Tables, Indexes, Constraints)          │
└──────────────────────────────────────────┘
```

### Project Structure

```
ExpenseManagement/
├── Properties/
│   ├── launchSettings.json          # Launch profiles for dev/prod
│   └── PublishProfiles/             # Publishing configurations
├── Controllers/                     # MVC Controllers
│   ├── AccountTeamController.cs     # Expense review workflows
│   ├── EmployeeController.cs        # Employee dashboards
│   ├── ManagementController.cs      # Management approvals
│   └── AccountController.cs         # Authentication
├── Views/                           # Razor Pages Templates
│   ├── AccountTeam/
│   ├── Employee/
│   ├── Management/
│   └── Shared/
│       ├── _Layout.cshtml           # Master layout
│       └── _SidebarNav.cshtml       # Navigation partial
├── Models/                          # Domain Models
│   ├── User.cs                      # User entity
│   ├── Budget.cs                    # Budget entity
│   ├── ExpenseReport.cs             # Expense report entity
│   ├── ExpenseClaim.cs              # Individual expense item
│   ├── WalletTransaction.cs         # Wallet ledger
│   ├── Company.cs                   # Company entity
│   └── Models.cs                    # Enums & constants
├── Data/                            # Data Access
│   ├── AppDbContext.cs              # Entity Framework DbContext
│   └── Migrations/                  # EF Core migrations
├── ViewModels/                      # View Models (DTOs)
│   └── ViewModels.cs                # Data transfer objects
├── Program.cs                       # Application startup configuration
├── appsettings.json                 # Configuration (development)
├── appsettings.Production.json       # Configuration (production)
├── Dockerfile                       # Docker container definition
└── ExpenseManagement.csproj         # Project file with dependencies
```

---

## Backend Technologies

### 1. ASP.NET Core 8

**What It Is**: Open-source, cross-platform web framework from Microsoft

**Key Components**:
- **Kestrel**: Built-in web server (handles HTTP/HTTPS)
- **Middleware Pipeline**: Request/response processing
- **Routing**: URL pattern matching
- **Dependency Injection**: IoC container

**Why Used**:
- ✅ LTS (Long-Term Support) - supported until November 2026
- ✅ High performance (one of fastest web frameworks)
- ✅ Cross-platform (Windows, Linux, macOS)
- ✅ Container-ready (Docker support)
- ✅ Built-in authentication

**Deployment Impact**:
- Requires .NET 8 Runtime on target server
- Kestrel handles requests directly or behind reverse proxy
- Minimal resource footprint (suitable for containers)

### 2. Razor Pages

**What It Is**: Page-based UI framework built on ASP.NET Core

**Key Features**:
```
Views\AccountTeam\ReportDetail.cshtml
├── @page directive              # Route handling
├── @model declaration           # Strongly-typed view model
├── @functions { }               # Code blocks
├── Razor syntax                 # C# mixed with HTML
└── Tag helpers                  # HTML+binding
```

**Comparison**:
| Feature | Razor Pages | MVC | Blazor |
|---------|------------|-----|--------|
| **Server-side** | ✅ Yes | ✅ Yes | ❌ No (WebAssembly) |
| **Productivity** | ✅ High | ⚠️ Medium | ✅ High |
| **Learning curve** | ✅ Low | ⚠️ Medium | ❌ Steep |
| **Routing** | Simple | Complex | N/A |
| **Performance** | Fast | Fast | Slower |

**Deployment Impact**:
- Views compiled at runtime or publish-time
- Minimal client-side JavaScript
- Full HTML rendered server-side

### 3. MVC Controllers

**What It Is**: Traditional MVC pattern for handling requests

**Key Controllers**:
- `AccountTeamController`: Expense report review workflows
- `EmployeeController`: Employee budget & expense management
- `ManagementController`: High-level approvals
- `AccountController`: Login, logout, user management

**Action Examples**:
```csharp
[HttpPost]
public async Task<IActionResult> ReviewExpense(int reportId, int claimId, string reviewAction)
{
    // Business logic
    await _db.SaveChangesAsync();
    return RedirectToAction("ReportDetail", new { id = reportId });
}
```

**Deployment Impact**:
- No compilation issues if code correct
- Routing registered in Program.cs
- Error handling affects user experience

---

## Frontend Technologies

### 1. HTML5 & CSS3

**What It Is**: Markup & styling standards

**Key Features Used**:
- Semantic HTML5 tags (`<section>`, `<article>`, `<nav>`)
- CSS Grid & Flexbox for layouts
- CSS Custom Properties (variables)
- Media queries for responsive design

**Example Layout**:
```html
<div class="d-flex gap-3 mb-4">
    <div class="col-6 col-md-3">
        <div class="stat-card">
            <div class="stat-label">Employee</div>
            <div class="fw-semibold">@Model.Employee?.FullName</div>
        </div>
    </div>
</div>
```

**Deployment Impact**:
- Browser compatibility: Modern browsers (Chrome, Firefox, Edge, Safari)
- No build step required (delivered as-is)
- CDN optimization recommended for CSS/JS

### 2. Bootstrap 5.3.2

**What It Is**: CSS framework for responsive web design

**Usage**:
- **Loaded via CDN**: `https://cdnjs.cloudflare.com/ajax/libs/bootstrap/5.3.2/css/bootstrap.min.css`
- **CSS Classes**: `btn`, `card`, `table`, `alert`, `badge`, etc.
- **Grid System**: 12-column responsive grid

**Key Features**:
- Responsive breakpoints: xs, sm, md, lg, xl, xxl
- Utility-first classes: `d-flex`, `gap-2`, `mb-3`, `px-4`
- Pre-built components: buttons, modals, dropdowns

**Deployment Impact**:
- CDN dependency - ensure internet access
- Fallback: Host Bootstrap locally if internet restricted
- Cache-friendly (immutable URLs)

### 3. Font Awesome 6.5.0

**What It Is**: Icon library

**Usage**:
- **Loaded via CDN**: `https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.0/css/all.min.css`
- **Usage**: `<i class="fas fa-check me-1"></i>` for icons

**Common Icons Used**:
- `fas fa-wallet` - Budget/wallet
- `fas fa-check` - Approve
- `fas fa-times` - Reject
- `fas fa-file-pdf` - PDF files
- `fas fa-download` - Downloads

**Deployment Impact**:
- CDN dependency
- ~100KB additional download
- Fallback: Use Unicode symbols if needed

### 4. JavaScript

**What It Is**: Client-side scripting language

**Usage**:
```javascript
// Inline scripts in Razor views
<script>
    function openRejectModal(claimId) {
        document.getElementById('rejectClaimId').value = claimId;
        new bootstrap.Modal(document.getElementById('rejectExpenseModal')).show();
    }

    // Session storage for scroll position
    const savedY = sessionStorage.getItem('reportDetailScrollY');
    if (savedY !== null) {
        window.scrollTo(0, parseInt(savedY, 10));
    }
</script>
```

**Key Features**:
- DOM manipulation (form handling)
- Modal dialogs (Bootstrap modals)
- Session storage (scroll position, temp data)
- Event listeners (form submission)

**Deployment Impact**:
- Client-side execution - no server resources
- No build/transpilation needed (vanilla JS)
- Works with all browsers supporting ES6+

---

## Database Technologies

### 1. SQL Server 2019+

**What It Is**: Enterprise-grade relational database from Microsoft

**Versions Support**:
| Version | Release | Support End | Compatibility |
|---------|---------|------------|--------------|
| SQL Server 2019 | Nov 2019 | Jan 2025 | ✅ Supported |
| SQL Server 2022 | Nov 2022 | Nov 2032 | ✅ Recommended |
| Azure SQL DB | Always latest | Always | ✅ Supported |

**Features Used**:
- **Windows Authentication**: Integrated security (on-premises)
- **SQL Authentication**: User/password (cloud)
- **T-SQL**: Stored procedures, views, triggers (if needed)
- **Transactions**: ACID compliance
- **Backup/Restore**: Data protection
- **Full-text search**: (Not currently used)
- **Indexes**: Performance optimization

**Database Schema**:
```sql
Tables:
├── Users                    # Employees, Admin, Management
├── Companies               # Multi-company support
├── Budgets                 # Allocated budgets per employee
├── ExpenseReports          # Expense report header
├── ExpenseClaims           # Individual expenses
├── WalletTransactions      # Wallet ledger entries
└── Notifications           # User notifications

Relationships:
- User → Company (many-to-one)
- Employee → Budgets (one-to-many)
- Budget → ExpenseReports (one-to-many)
- ExpenseReport → ExpenseClaims (one-to-many)
- User → WalletTransactions (one-to-many)
```

**Deployment Impact**:
- Requires SQL Server instance or Azure SQL
- Connection string configuration critical
- Migrations applied at application startup
- Backup strategy essential

### 2. Entity Framework Core 8.0.0

**What It Is**: Object-Relational Mapper (ORM) for .NET

**Key Components**:

#### DbContext
```csharp
public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Budget> Budgets { get; set; }
    public DbSet<ExpenseReport> ExpenseReports { get; set; }
    // ... more DbSets

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Fluent API configuration
        modelBuilder.Entity<User>()
            .HasMany(u => u.Budgets)
            .WithOne(b => b.Employee)
            .HasForeignKey(b => b.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

#### Code-First Approach
```csharp
// Define model
public class Budget
{
    public int Id { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime AllocatedOn { get; set; }
}

// Generate migration
// dotnet ef migrations add AddBudget

// Apply to database
// dotnet ef database update
```

**Benefits**:
- ✅ No separate SQL code for CRUD
- ✅ Type-safe queries (LINQ)
- ✅ Automatic relationship management
- ✅ Migrations for version control
- ✅ Performance (lazy loading, eager loading)

**Deployment Impact**:
- Migrations auto-run on application startup
- LINQ queries translated to T-SQL at runtime
- Lazy loading can cause N+1 query problems (mitigated with .Include())
- Connection pooling reduces overhead

### 3. SQL Server NuGet Package

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="8.0.0" />
```

**What It Does**:
- EF Core provider for SQL Server
- Translates LINQ → T-SQL
- Handles SQL Server-specific features
- Manages connections & connection pooling

---

## Authentication & Security

### 1. Cookie-Based Authentication

**How It Works**:
```
User Login → Credentials Validated → Session Token → Cookie Set → Subsequent Requests Include Cookie
```

**Configuration**:
```csharp
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);  // 12-hour session
    });
```

**Security Features**:
- HttpOnly cookies (prevents JavaScript access)
- Secure flag (HTTPS only in production)
- SameSite attribute (CSRF protection)
- Automatic renewal (sliding expiration)

**Login Flow**:
```
1. User submits credentials to /Account/Login
2. Database lookup (Users table)
3. BCrypt password verification
4. Claims created (UserId, FullName, Role, CompanyName)
5. Authentication cookie issued
6. Redirect to dashboard
```

**Role-Based Access Control (RBAC)**:
```csharp
[Authorize(Roles = "AccountTeam")]
public IActionResult ReviewExpense()
{
    // Only AccountTeam users can access
}

// In views
@if (User.IsInRole("Management"))
{
    <button>Approve Report</button>
}
```

### 2. BCrypt Password Hashing

**NuGet Package**:
```xml
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
```

**How It Works**:
```csharp
// During registration/password change
string hashedPassword = BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 11);

// During login
bool isPasswordValid = BCrypt.Net.BCrypt.Verify(plainPassword, hashedPassword);
```

**Security**:
- ✅ Irreversible (one-way hashing)
- ✅ Salt included (prevents rainbow tables)
- ✅ Adaptive (slows down brute-force attacks)
- ✅ Industry standard (used by major platforms)

**Deployment Impact**:
- No plain passwords stored
- Secure even if database compromised
- CPU-intensive (intentionally, for security)

### 3. HTTPS/TLS

**Configuration**:
```csharp
app.UseHttpsRedirection();  // Redirect HTTP → HTTPS
app.UseHsts();              // HTTP Strict Transport Security
```

**Security Headers** (in web.config or middleware):
```
Strict-Transport-Security: max-age=31536000; includeSubDomains
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
```

**Deployment Impact**:
- SSL/TLS certificate required (Let's Encrypt, DigiCert, etc.)
- Certificate renewal annually (or use auto-renewal)
- Port 443 (HTTPS) must be accessible
- Adds ~5-10ms latency (negligible)

---

## Data Processing & Export

### 1. ClosedXML 0.104.1

**What It Is**: Library for creating/reading Excel files (.xlsx)

**Usage**:
```csharp
using (var workbook = new XLWorkbook())
{
    var worksheet = workbook.Worksheets.Add("Expenses");
    worksheet.Cell(1, 1).Value = "Date";
    worksheet.Cell(1, 2).Value = "Amount";
    worksheet.Cell(2, 1).Value = DateTime.Now;
    worksheet.Cell(2, 2).Value = 1000.50;

    workbook.SaveAs("report.xlsx");
}
```

**Features**:
- ✅ Create complex spreadsheets
- ✅ Styling (colors, fonts, borders)
- ✅ Formulas (SUM, AVERAGE, etc.)
- ✅ Multiple sheets
- ✅ Data validation
- ✅ Charts (optional)

**Deployment Impact**:
- Generates files server-side (minimal client impact)
- Large exports (10,000+ rows) may use RAM
- Consider async generation for heavy exports

### 2. SixLabors.ImageSharp 3.1.5

**What It Is**: Image processing library for .NET

**Usage**:
```csharp
using (var image = Image.Load(uploadedStream))
{
    image.Mutate(x => x.Resize(300, 300));  // Resize
    image.Save(savePath);  // Save optimized
}
```

**Features**:
- ✅ Image resizing
- ✅ Rotation, cropping
- ✅ Format conversion (JPG, PNG, WebP)
- ✅ Compression
- ✅ Metadata reading

**Deployment Impact**:
- Receipt image optimization on upload
- Reduces storage requirements
- CPU-intensive for large uploads

### 3. PDF Handling

**Current Implementation**:
- Receipt PDFs stored as files (not database BLOBs)
- Displayed via `<a href="receipt.pdf" target="_blank">`
- No server-side PDF generation (currently)

**If PDF Generation Added**:
- Use iTextSharp or SelectPdf
- Consider server-side rendering
- Monitor disk space for generated PDFs

---

## Deployment Technologies

### 1. Docker

**What It Is**: Containerization platform for consistent deployment

**Multi-Stage Build**:
```dockerfile
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5000
ENTRYPOINT ["dotnet", "ExpenseManagement.dll"]
```

**Benefits**:
- ✅ Same environment (dev, staging, prod)
- ✅ Easy scaling (run multiple containers)
- ✅ Rollback capability (tagged images)
- ✅ Simplified deployment process

**Deployment Impact**:
- Requires Docker runtime on host
- Container orchestration (Kubernetes, Docker Compose)
- Network isolation between containers
- Persistent storage handling (volumes)

### 2. Docker Compose

**What It Is**: Multi-container orchestration tool

**Example**:
```yaml
version: '3.8'

services:
  app:
    build: .
    ports:
      - "5000:5000"
    environment:
      ConnectionStrings__DefaultConnection: "Server=db;..."
    depends_on:
      - db

  db:
    image: mcr.microsoft.com/mssql/server:2019-latest
    environment:
      SA_PASSWORD: "YourPassword"
    ports:
      - "1433:1433"
```

**Deployment Impact**:
- Single command deployment: `docker-compose up -d`
- Automatic networking between containers
- Service dependencies management

### 3. Azure App Service

**What It Is**: PaaS (Platform-as-a-Service) for web apps

**Key Features**:
- Automatic scaling (auto-scale based on CPU/memory)
- Built-in CI/CD (GitHub, Azure DevOps)
- Managed SSL certificates
- Application Insights integration
- Staging slots for zero-downtime deployment

**Deployment Models**:
1. **Code push** (Git integration)
   ```bash
   git push origin master  # Auto-deploys
   ```

2. **ZIP deployment** (Azure CLI)
   ```bash
   az webapp deployment source config-zip \
     --name app-name \
     --src deployment.zip
   ```

3. **Docker image** (Azure Container Registry)
   ```bash
   docker push containerregistry.azurecr.io/expensemanagement:latest
   ```

**Deployment Impact**:
- Pay-per-use pricing (B1-P1V2 plans)
- Auto-restart on failure
- HTTP/2 support
- WebSocket support

---

## Third-Party Dependencies

### NuGet Packages

| Package | Version | Purpose | Deployment Impact |
|---------|---------|---------|------------------|
| **Microsoft.EntityFrameworkCore** | 8.0.0 | ORM | Requires DB provider |
| **Microsoft.EntityFrameworkCore.SqlServer** | 8.0.0 | SQL Server provider | Connects to DB |
| **Microsoft.AspNetCore.Authentication.Cookies** | 2.2.0 | Authentication | Session management |
| **BCrypt.Net-Next** | 4.0.3 | Password hashing | Security |
| **ClosedXML** | 0.104.1 | Excel export | File generation |
| **SixLabors.ImageSharp** | 3.1.5 | Image processing | CPU usage |

### CDN Dependencies

| Resource | URL | Size | Impact |
|----------|-----|------|--------|
| **Bootstrap CSS** | cdnjs.cloudflare.com | ~180 KB | UI framework |
| **Font Awesome** | cdnjs.cloudflare.com | ~100 KB | Icons |
| **Bootstrap JS** | cdnjs.cloudflare.com | ~80 KB | Interactive features |

**Deployment Impact**:
- CDN outage → UI breaks
- Fallback: Self-host if internet restricted
- Monitor CDN performance (latency)

---

## Integration Points

### 1. Active Directory / LDAP (Optional)

**Current Implementation**: SQL Server-based users

**If AD Integration Needed**:
```csharp
services.AddAuthentication(AuthenticationSchemes.Negotiate);
```

### 2. Email Notifications (Optional)

**Current**: None implemented

**If Email Added**:
```csharp
// SendGrid, Mailgun, or SMTP
services.AddSingleton<IEmailSender>(new EmailSender(
    smtpServer, smtpPort, fromAddress));
```

**Deployment Impact**:
- Requires email server access
- SMTP credentials needed
- Email quota limits

### 3. Azure Key Vault (For Secrets)

**If Implemented**:
```csharp
var keyVaultEndpoint = new Uri(Environment.GetEnvironmentVariable("KEYVAULT_ENDPOINT"));
var credential = new DefaultAzureCredential();
var client = new SecretClient(keyVaultEndpoint, credential);
var connectionString = client.GetSecret("DatabaseConnectionString").Value.Value;
```

**Deployment Impact**:
- Centralized secret management
- No secrets in code/config files
- Azure managed identity required

### 4. Application Insights (Monitoring)

**If Configured**:
```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

**Tracks**:
- Request/response times
- Exception tracking
- Dependency monitoring (DB queries)
- Custom events

**Deployment Impact**:
- Real-time monitoring dashboard
- Performance diagnostics
- Cost (~$0.30/GB for ingestion)

---

## Version Compatibility Matrix

### .NET Versions

```
.NET Version    Release Date    EOL Date        LTS    Current Status
.NET 8          Nov 2023        Nov 2026        ✅     Active (Used in this project)
.NET 9          Nov 2024        May 2025        ❌     Latest
.NET 7          Nov 2022        May 2024        ❌     EOL
.NET 6          Nov 2021        Nov 2024        ✅     Approaching EOL
```

### SQL Server Versions

```
SQL Version         Release     EOL             Compatibility
SQL Server 2019     Nov 2019    Jan 2025        ✅ Supported
SQL Server 2022     Nov 2022    Nov 2032        ✅ Recommended
Azure SQL Database  Always      Always          ✅ Full support
```

### Browser Support

```
Browser             Minimum Version    Support Status
Chrome              Latest 2 versions  ✅ Full
Firefox             Latest 2 versions  ✅ Full
Safari              Latest 2 versions  ✅ Full
Edge                Latest 2 versions  ✅ Full
IE 11               -                  ❌ Not supported
```

---

## Technology Decision Rationale

### Why .NET 8?

| Factor | Reason |
|--------|--------|
| **LTS** | Support until Nov 2026 |
| **Performance** | 40% faster than .NET 6 |
| **Features** | LINQ improvements, collections |
| **Cloud** | Azure-native optimizations |
| **Industry** | Most adopted .NET version |
| **Containers** | Excellent Docker support |

### Why Razor Pages over MVC?

| Factor | Razor Pages | MVC |
|--------|-------------|-----|
| **Learning Curve** | Low ✅ | Medium |
| **Folder Structure** | Simple | Complex |
| **Productivity** | High ✅ | Medium |
| **Scalability** | Good | Excellent |
| **Team Size** | Small ✅ | Large |

**Decision**: Chosen for team productivity & simpler maintenance

### Why SQL Server?

| Factor | SQL Server | PostgreSQL | MySQL |
|--------|-----------|-----------|--------|
| **Enterprise** | ✅ Yes | ⚠️ Good | Limited |
| **Azure** | ✅ Native | Limited | Limited |
| **Support** | ✅ Enterprise | Community | Community |
| **Features** | ✅ Complete | Good | Limited |
| **Cost** | $$$ | Free | Free |

**Decision**: Enterprise support + Azure integration needed

### Why Docker?

| Benefit | Impact |
|---------|--------|
| **Consistency** | Dev/Prod parity |
| **Scalability** | Horizontal scaling easy |
| **Deployment** | Single command deploy |
| **Portability** | Runs anywhere |

---

## Deployment Tech Impact

### For On-Premises (Windows Server + IIS)

```
Technology          Requirement              Deployment Complexity
.NET 8 Runtime      Install ASP.NET Hosting  Low - Single MSI
IIS                 Windows Server 2016+     Low - Built-in
SQL Server          Separate instance        Medium - DB setup
HTTPS Certificate   SSL/TLS cert            Low - Certificate store
```

### For Docker

```
Technology          Requirement              Deployment Complexity
Docker Engine       Runtime installed        Medium - Container mgmt
docker-compose      Orchestration tool       Medium - Config file
SQL Server image    Container image          Low - Pre-built
Network setup       Bridge network           Low - Automatic
```

### For Azure

```
Technology          Requirement              Deployment Complexity
Azure Subscription  Active account          Low - Web portal
App Service Plan    Compute resources       Low - SKU selection
Azure SQL Database  Managed DB              Low - Point & click
Key Vault           Secret storage          Medium - Configuration
```

### Performance Implications

| Technology | CPU Usage | Memory | Disk | Network |
|-----------|-----------|--------|------|---------|
| .NET 8 Runtime | ~30% idle | 200-400 MB | 100 MB | Minimal |
| EF Core | Variable | +50-200 MB | - | Dependent |
| SQL Server | 20-40% | 500 MB - 2 GB | 5+ GB | Local/Network |
| ImageSharp | 50%+ (during upload) | +100-500 MB | Variable | Minimal |
| ClosedXML | 30-80% (export) | +100-500 MB | Variable | Minimal |

### Scaling Considerations

**Vertical Scaling** (single server):
- .NET 8: Handles 10,000+ req/sec per CPU core
- SQL Server: Add more CPUs/RAM as needed
- Max practical server: 64 GB RAM, 32 cores

**Horizontal Scaling** (multiple servers):
- Docker containers: Deploy multiple replicas
- Load balancer: Distribute requests
- Shared database: Single SQL Server instance
- Session state: Use distributed cache (Redis)

---

## Technology Recommendations

### For Development
- **IDE**: Visual Studio 2026 Community (Free)
- **Database**: SQL Server Express (Free)
- **Containers**: Docker Desktop (Free)

### For Staging
- **Hosting**: Azure App Service (B1 plan ~$10/month)
- **Database**: Azure SQL (Basic ~$5/month)
- **Configuration**: Environment parity with production

### For Production
- **Hosting**: Azure App Service (B2 or P1V2 plan)
- **Database**: Azure SQL (Standard or Premium)
- **Monitoring**: Application Insights
- **Backup**: Automatic (Azure backup service)
- **Redundancy**: Multiple regions for HA

---

## Troubleshooting by Technology

### .NET Runtime Issues
```powershell
# Check installed version
dotnet --version

# Check available runtimes
dotnet --info

# Run specific runtime version
dotnet run --version 8.0.0
```

### SQL Server Connection Issues
```sql
-- Test connectivity
SELECT 1

-- Check login
SELECT * FROM sys.server_principals

-- Verify database exists
SELECT name FROM sys.databases WHERE name='ExpenseManagementDB'
```

### Docker Issues
```bash
# Check image build
docker build -t expensemanagement:latest .

# Check container logs
docker logs -f expensemanagement-app

# Check resource usage
docker stats
```

### EF Core Migration Issues
```bash
# Check pending migrations
dotnet ef migrations list

# Remove last migration
dotnet ef migrations remove

# Update database
dotnet ef database update
```

---

## Security Checklist by Technology

- [ ] **.NET**: Keep runtime updated to latest patch
- [ ] **SQL Server**: Use strong SA password, enable Windows Auth
- [ ] **EF Core**: Use parameterized queries (automatic)
- [ ] **Authentication**: HTTPS only, HttpOnly cookies
- [ ] **Passwords**: BCrypt with workFactor ≥ 11
- [ ] **HTTPS**: Valid certificate, auto-renewal configured
- [ ] **Docker**: Non-root user in container, minimal base image
- [ ] **Secrets**: Use Key Vault, never commit credentials
- [ ] **CORS**: Restrict origins, no wildcard in production
- [ ] **Headers**: HSTS, CSP, X-Frame-Options configured

---

## Performance Tuning by Technology

### .NET/ASP.NET Core
```csharp
// Use async/await
public async Task<IActionResult> GetReports()
{
    var reports = await _db.ExpenseReports.ToListAsync();
    return Ok(reports);
}

// Enable response compression
services.AddResponseCompression();

// Configure connection pooling
services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connString, 
        x => x.MaxBatchSize(1000)));
```

### Entity Framework Core
```csharp
// Use .Include() to prevent N+1 queries
var reports = await _db.ExpenseReports
    .Include(r => r.Employee)
    .Include(r => r.ExpenseClaims)
    .ToListAsync();

// Use AsNoTracking() for read-only queries
var reports = await _db.ExpenseReports
    .AsNoTracking()
    .ToListAsync();
```

### SQL Server
```sql
-- Add indexes for frequently queried columns
CREATE INDEX IX_ExpenseReports_Status ON ExpenseReports(Status);

-- Monitor query performance
SELECT * FROM sys.dm_exec_requests;

-- Update statistics
EXEC sp_updatestats;
```

---

## Monitoring by Technology

| Technology | Metric | Tool | Alert Threshold |
|-----------|--------|------|-----------------|
| .NET Runtime | CPU, Memory | Task Manager / Docker stats | >80% |
| SQL Server | Query time, Locks | SQL Server Profiler | >1 sec |
| HTTPS | Certificate expiry | Cert monitoring | 30 days |
| Application | Error rate | Application Insights | >1% |
| Disk | Free space | Server monitoring | <10% free |

---

**Document Version**: 1.0  
**Last Updated**: 2024  
**Technology Stack Version**: .NET 8, SQL Server 2019+, Docker  
**Target Audience**: Deployment engineers, DevOps, architects
