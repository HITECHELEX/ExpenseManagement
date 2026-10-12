# ExpenseManagement - Deployment Guide

## Table of Contents
1. [Overview](#overview)
2. [System Requirements](#system-requirements)
3. [Pre-Deployment Checklist](#pre-deployment-checklist)
4. [Deployment Options](#deployment-options)
5. [Local/On-Premises Deployment](#localon-premises-deployment)
6. [Docker Deployment](#docker-deployment)
7. [Azure Deployment](#azure-deployment)
8. [Configuration](#configuration)
9. [Database Setup](#database-setup)
10. [Troubleshooting](#troubleshooting)
11. [Monitoring & Maintenance](#monitoring--maintenance)

---

## Overview

**ExpenseManagement** is an ASP.NET Core 8 Razor Pages application for managing employee expense reports and budgets across multiple companies.

### Tech Stack
- **Framework**: .NET 8 (LTS)
- **UI**: ASP.NET Core Razor Pages + Bootstrap 5.3.2
- **Database**: SQL Server 2019+
- **ORM**: Entity Framework Core 8.0
- **Auth**: Cookie-based Authentication
- **Container**: Docker with multi-stage builds
- **Export**: Excel (ClosedXML), PDF support

### Key Features
- Multi-company support
- Budget allocation & tracking
- Expense report workflow (Employee → AccountTeam → Management)
- Approval workflow with remarks
- Wallet balance management
- Excel/PDF export

---

## System Requirements

### For Development
- **OS**: Windows 10/11, macOS, or Linux
- **.NET SDK**: 8.0.x or later
- **Visual Studio**: 2022/2026 Community Edition or higher
- **SQL Server**: 2019+ or SQL Server Express
- **Git**: Latest version
- **Docker** (optional): Docker Desktop for containerization

### For Production
- **OS**: Windows Server 2016+ or Linux (with Docker)
- **.NET Runtime**: 8.0.x ASP.NET Core
- **SQL Server**: 2019+ (Enterprise/Standard) or Azure SQL
- **Web Server**: IIS 10+ (Windows) or reverse proxy (Linux)
- **SSL Certificate**: Valid HTTPS certificate
- **Disk Space**: Minimum 5 GB
- **RAM**: Minimum 4 GB (8 GB recommended)
- **Network**: Open ports 80 (HTTP) and 443 (HTTPS)

---

## Pre-Deployment Checklist

- [ ] Code is committed to Git (master branch)
- [ ] All unit tests pass locally (`dotnet test`)
- [ ] Build succeeds without warnings (`dotnet build`)
- [ ] Database migrations are tested (`dotnet ef database update`)
- [ ] `appsettings.json` is updated for target environment
- [ ] Secrets are NOT committed to Git
- [ ] SSL certificates obtained (for HTTPS)
- [ ] Database backup strategy planned
- [ ] Monitoring/logging configured
- [ ] Load balancer configured (if multi-server setup)
- [ ] Firewall rules reviewed and applied
- [ ] Backup & disaster recovery plan in place

---

## Deployment Options

| Option | Best For | Complexity | Cost |
|--------|----------|-----------|------|
| **Local/IIS** | Small teams, internal use | Low | $0 - $500 |
| **Docker + Linux VM** | Scalability, multi-platform | Medium | $50 - $200/mo |
| **Azure App Service** | Enterprise, managed cloud | Low | $100 - $500/mo |
| **AWS Elastic Beanstalk** | Enterprise, AWS ecosystem | Low | $80 - $400/mo |

---

## Local/On-Premises Deployment

### Windows Server + IIS

#### Step 1: Install Prerequisites

```powershell
# Install .NET 8 Runtime (ASP.NET Core Hosting Bundle)
# Download from: https://dotnet.microsoft.com/download/dotnet/8.0
# Choose "Hosting Bundle" for Windows

# Verify installation
dotnet --version
```

#### Step 2: Build Release Package

```powershell
cd E:\ExpenseManagement_v6_Final\v2\exp_working\exp_work

# Restore & publish
dotnet restore
dotnet publish -c Release -o C:\inetpub\wwwroot\ExpenseManagement
```

#### Step 3: Configure IIS

1. Open **IIS Manager**
2. Right-click **Sites** → **Add Website**
3. Configure:
   - **Site name**: `ExpenseManagement`
   - **Physical path**: `C:\inetpub\wwwroot\ExpenseManagement`
   - **Binding**: 
     - HTTP: `80` (optional)
     - HTTPS: `443` (required for production)
   - **SSL Certificate**: Select your SSL certificate

4. Configure **Application Pool**:
   - Right-click new app pool → **Edit**
   - **.NET CLR version**: No Managed Code
   - **Managed pipeline mode**: Integrated

#### Step 4: Configure appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SQL_SERVER;Database=ExpenseManagementDB;User Id=sa;Password=YourPassword;TrustServerCertificate=True"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "yourdomain.com;www.yourdomain.com"
}
```

#### Step 5: Set Permissions

```powershell
# Grant IIS AppPool user read/write permissions
$appPoolIdentity = "IIS AppPool\ExpenseManagement"
$path = "C:\inetpub\wwwroot\ExpenseManagement"

icacls $path /grant "$appPoolIdentity:(OI)(CI)RWX" /inheritance:e
```

#### Step 6: Test Deployment

- Navigate to `https://yourdomain.com`
- Login with test credentials
- Verify database connectivity

---

## Docker Deployment

### Docker on Windows/macOS/Linux

#### Step 1: Build Docker Image

```bash
cd E:\ExpenseManagement_v6_Final\v2\exp_working\exp_work

# Build image
docker build -t expensemanagement:latest .

# Verify image
docker images | grep expensemanagement
```

#### Step 2: Run Container with External SQL Server

```bash
docker run -d \
  --name expensemanagement \
  -p 5000:5000 \
  -e ConnectionStrings__DefaultConnection="Server=YOUR_SQL_SERVER;Database=ExpenseManagementDB;User Id=sa;Password=YourPassword;TrustServerCertificate=True" \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ASPNETCORE_URLS=http://+:5000 \
  expensemanagement:latest
```

#### Step 3: Using Docker Compose (Recommended)

Create `docker-compose.yml`:

```yaml
version: '3.8'

services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2019-latest
    container_name: expensemanagement-db
    environment:
      ACCEPT_EULA: Y
      SA_PASSWORD: YourStrongPassword123!
    ports:
      - "1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql
    networks:
      - expensemanagement-net

  app:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: expensemanagement-app
    depends_on:
      - sqlserver
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:5000
      ConnectionStrings__DefaultConnection: "Server=sqlserver;Database=ExpenseManagementDB;User Id=sa;Password=YourStrongPassword123!;TrustServerCertificate=True"
    ports:
      - "5000:5000"
    networks:
      - expensemanagement-net
    restart: unless-stopped

volumes:
  sqlserver_data:

networks:
  expensemanagement-net:
    driver: bridge
```

Deploy:

```bash
docker-compose up -d
```

#### Step 4: Access Application

- Navigate to `http://localhost:5000`
- Login with credentials

#### Step 5: View Logs

```bash
docker logs -f expensemanagement-app
```

---

## Azure Deployment

### Using Azure App Service + Azure SQL Database

#### Step 1: Create Resources in Azure Portal

```bash
# Login to Azure
az login

# Create Resource Group
az group create --name ExpenseManagement-RG --location eastus

# Create App Service Plan
az appservice plan create \
  --name ExpenseManagement-Plan \
  --resource-group ExpenseManagement-RG \
  --sku B2 --is-linux

# Create Web App
az webapp create \
  --name expensemanagement-app \
  --resource-group ExpenseManagement-RG \
  --plan ExpenseManagement-Plan \
  --runtime "DOTNETCORE|8.0"

# Create Azure SQL Server
az sql server create \
  --name expensemanagement-server \
  --resource-group ExpenseManagement-RG \
  --location eastus \
  --admin-user sqladmin \
  --admin-password YourStrongPassword123!

# Create Azure SQL Database
az sql db create \
  --server expensemanagement-server \
  --resource-group ExpenseManagement-RG \
  --name ExpenseManagementDB \
  --edition Basic
```

#### Step 2: Configure Connection String

```bash
az webapp config connection-string set \
  --name expensemanagement-app \
  --resource-group ExpenseManagement-RG \
  --settings DefaultConnection="Server=tcp:expensemanagement-server.database.windows.net,1433;Initial Catalog=ExpenseManagementDB;Persist Security Info=False;User ID=sqladmin;Password=YourStrongPassword123!;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" \
  --connection-string-type SQLServer
```

#### Step 3: Deploy Application

```bash
# Publish to Azure
dotnet publish -c Release -o ./publish

# Deploy using ZIP
cd publish
Compress-Archive -Path * -DestinationPath ../expensemanagement.zip
cd ..

az webapp deployment source config-zip \
  --resource-group ExpenseManagement-RG \
  --name expensemanagement-app \
  --src expensemanagement.zip
```

#### Step 4: Configure Application Settings

```bash
az webapp config appsettings set \
  --name expensemanagement-app \
  --resource-group ExpenseManagement-RG \
  --settings \
    ASPNETCORE_ENVIRONMENT=Production \
    WEBSITES_ENABLE_APP_SERVICE_STORAGE=true
```

#### Step 5: Access Application

- Navigate to `https://expensemanagement-app.azurewebsites.net`

---

## Configuration

### appsettings.json Structure

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SERVER;Database=ExpenseManagementDB;..."
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

### appsettings.Production.json (Secrets)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=prod-server;Database=ExpenseManagementDB;User Id=produser;Password=VAULT_SECURE_PASSWORD;TrustServerCertificate=False"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning"
    }
  }
}
```

**Never commit production credentials to Git!** Use:
- Azure Key Vault
- AWS Secrets Manager
- Environment variables
- appsettings.Production.json (in .gitignore)

---

## Database Setup

### SQL Server Database Creation

```sql
-- Create database
CREATE DATABASE ExpenseManagementDB;

-- Create login
CREATE LOGIN produser WITH PASSWORD = 'StrongPassword123!';

-- Switch to database
USE ExpenseManagementDB;

-- Create user
CREATE USER produser FOR LOGIN produser;

-- Grant permissions
ALTER ROLE db_owner ADD MEMBER produser;
```

### Run EF Core Migrations

```bash
# Update database (auto-runs on app startup, but can run manually)
dotnet ef database update

# Or from Package Manager Console in Visual Studio
Update-Database
```

### Verify Database Schema

```sql
-- Check tables created
SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo';

-- Check indexes
SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Users');
```

---

## Troubleshooting

### Common Issues

#### 1. Database Connection Fails

**Error**: `A network-related or instance-specific error occurred...`

**Solution**:
```bash
# Test SQL Server connectivity
sqlcmd -S YOUR_SERVER -U your_user -P your_password -d ExpenseManagementDB -Q "SELECT 1"

# Verify connection string format
# Windows Auth: Server=SERVER;Database=DB;Trusted_Connection=True;
# SQL Auth: Server=SERVER;Database=DB;User Id=user;Password=pass;
```

#### 2. EF Core Migration Fails

**Error**: `The model for context 'AppDbContext' has changed...`

**Solution**:
```bash
# Remove pending migrations
dotnet ef migrations remove

# Add new migration
dotnet ef migrations add InitialCreate

# Update database
dotnet ef database update
```

#### 3. Authentication Not Working

**Error**: `Login page loops or 401 Unauthorized`

**Solution**:
```csharp
// Verify in Program.cs
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
    });
```

#### 4. HTTPS Certificate Error

**Error**: `The certificate is not trusted`

**Solution**:
```bash
# For IIS
# 1. Obtain valid certificate (Let's Encrypt or commercial CA)
# 2. Import into Windows Certificate Store
# 3. Bind in IIS under HTTPS

# For Docker
docker run -d \
  -p 443:443 \
  -v /path/to/cert.pfx:/app/cert.pfx \
  -e ASPNETCORE_Kestrel__Certificates__Default__Path=/app/cert.pfx \
  -e ASPNETCORE_Kestrel__Certificates__Default__Password=cert_password \
  expensemanagement:latest
```

#### 5. Application Crashes on Startup

**Error**: `Exception: One or more errors occurred...`

**Solution**:
```bash
# Check logs
docker logs expensemanagement-app

# Run locally to debug
dotnet run --environment Development

# Check appsettings.json syntax
# Verify all connection strings are correct
```

---

## Monitoring & Maintenance

### Application Health Monitoring

#### Using Application Insights (Azure)

```csharp
// Add to Program.cs
builder.Services.AddApplicationInsightsTelemetry();
```

#### Using Serilog (Any Platform)

```bash
dotnet add package Serilog
dotnet add package Serilog.Sinks.File
dotnet add package Serilog.Sinks.Console
```

```csharp
// Program.cs
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File("logs/app-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();
```

### Backup Strategy

#### SQL Server Automated Backups

```sql
-- Full backup daily
BACKUP DATABASE ExpenseManagementDB 
TO DISK = 'C:\Backups\ExpenseManagementDB_full.bak'
WITH INIT, COMPRESSION;

-- Transaction log backup every hour
BACKUP LOG ExpenseManagementDB 
TO DISK = 'C:\Backups\ExpenseManagementDB_log.trn'
WITH INIT, COMPRESSION;
```

#### Docker Volume Backups

```bash
# Backup SQL Server data volume
docker run --rm \
  -v expensemanagement-db:/dbdata \
  -v C:\Backups:/backup \
  mcr.microsoft.com/mssql/server:2019-latest \
  tar czf /backup/db-backup.tar.gz /dbdata

# Restore from backup
docker run --rm \
  -v expensemanagement-db:/dbdata \
  -v C:\Backups:/backup \
  mcr.microsoft.com/mssql/server:2019-latest \
  tar xzf /backup/db-backup.tar.gz -C /
```

### Performance Monitoring

#### Monitor Key Metrics

- **CPU Usage**: Should be < 70% under normal load
- **Memory**: Monitor for leaks (should stabilize)
- **Disk**: Ensure log files don't fill disk
- **Database Connections**: Monitor active connections
- **Response Times**: Track endpoint latency

```bash
# Docker stats
docker stats expensemanagement-app

# Windows Performance Monitor
perfmon
```

### Regular Maintenance Tasks

- [ ] **Daily**: Monitor logs for errors
- [ ] **Weekly**: Review backup success
- [ ] **Monthly**: Update NuGet packages (`dotnet outdated`)
- [ ] **Quarterly**: Security patches & OS updates
- [ ] **Annually**: Disaster recovery drill, capacity planning

---

## Rollback Procedure

### If Deployment Goes Wrong

#### IIS Rollback

```powershell
# Restore previous version
Copy-Item -Path "C:\inetpub\wwwroot\ExpenseManagement.backup" `
          -Destination "C:\inetpub\wwwroot\ExpenseManagement" -Recurse -Force

# Restart IIS
iisreset /restart
```

#### Docker Rollback

```bash
# Stop current container
docker stop expensemanagement-app

# Run previous image tag
docker run -d \
  --name expensemanagement-app \
  -p 5000:5000 \
  expensemanagement:v1.0  # Use previous tag
```

#### Database Rollback

```bash
# Revert to previous migration
dotnet ef database update PreviousMigrationName

# Or restore from backup
RESTORE DATABASE ExpenseManagementDB 
FROM DISK = 'C:\Backups\ExpenseManagementDB_full.bak'
WITH REPLACE;
```

---

## Support & Documentation

- **GitHub Repo**: https://github.com/HITECHELEX/ExpenseManagement
- **Microsoft Docs**: https://learn.microsoft.com/dotnet/core/
- **EF Core Docs**: https://learn.microsoft.com/ef/core/
- **Azure Docs**: https://learn.microsoft.com/azure/

---

## Appendix: Quick Commands

```bash
# Build & Test Locally
dotnet restore
dotnet build
dotnet test
dotnet run

# Publish Release
dotnet publish -c Release -o ./publish

# Docker Commands
docker build -t expensemanagement:latest .
docker run -d -p 5000:5000 expensemanagement:latest
docker-compose up -d
docker logs -f container-name
docker stop container-name
docker rm container-name

# Database Commands
dotnet ef migrations add MigrationName
dotnet ef database update
dotnet ef database update 0  # Revert all

# Git Commands
git status
git add .
git commit -m "Deploy: Production release v1.0"
git push origin master
```

---

**Document Version**: 1.0  
**Last Updated**: 2024  
**Maintained By**: Development Team  
**Status**: Active
