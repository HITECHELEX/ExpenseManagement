# Production Configuration Template

## appsettings.Production.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_PRODUCTION_SERVER;Database=ExpenseManagementDB;User Id=produser;Password=YOUR_SECURE_PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  },
  "AllowedHosts": "yourdomain.com;www.yourdomain.com;api.yourdomain.com"
}
```

## Environment Variables (Docker/Azure)

```bash
# Required
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:5000
ConnectionStrings__DefaultConnection=Server=...;Database=ExpenseManagementDB;...

# Optional but Recommended
ASPNETCORE_Kestrel__Certificates__Default__Path=/app/cert.pfx
ASPNETCORE_Kestrel__Certificates__Default__Password=your_cert_password
```

## IIS Configuration (web.config)

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <!-- ASP.NET Core Module Configuration -->
    <aspNetCore 
      processPath="dotnet" 
      arguments=".\ExpenseManagement.dll" 
      stdoutLogEnabled="false" 
      stdoutLogFile=".\logs\stdout" 
      hostingModel="inprocess">
      <environmentVariables>
        <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
        <environmentVariable name="ASPNETCORE_URLS" value="http://localhost:5000" />
      </environmentVariables>
    </aspNetCore>

    <!-- Security Headers -->
    <httpProtocol>
      <customHeaders>
        <add name="Strict-Transport-Security" value="max-age=31536000; includeSubDomains" />
        <add name="X-Content-Type-Options" value="nosniff" />
        <add name="X-Frame-Options" value="DENY" />
        <add name="X-XSS-Protection" value="1; mode=block" />
        <add name="Referrer-Policy" value="strict-origin-when-cross-origin" />
      </customHeaders>
    </httpProtocol>

    <!-- Compression -->
    <urlCompression doDynamicCompression="true" doStaticCompression="true" />

    <!-- Static File Caching -->
    <staticContent>
      <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="30.00:00:00" />
    </staticContent>

    <!-- HTTPS Redirect -->
    <rewrite>
      <rules>
        <rule name="HTTP to HTTPS Redirect" stopProcessing="true">
          <match url="(.*)" />
          <conditions>
            <add input="{HTTPS}" pattern="^OFF$" />
          </conditions>
          <action type="Redirect" url="https://{HTTP_HOST}{REQUEST_URI}" redirectType="Permanent" />
        </rule>
      </rules>
    </rewrite>

    <!-- Request Limits -->
    <security>
      <requestFiltering>
        <requestLimits maxAllowedContentLength="104857600" /> <!-- 100 MB -->
      </requestFiltering>
    </security>

    <!-- Logging -->
    <httpLogging directory="C:\inetpub\logs\http" dontLog="false" />
  </system.webServer>
</configuration>
```

## Docker Configuration

### Dockerfile (Production)

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy project files
COPY ["ExpenseManagement.csproj", "./"]
RUN dotnet restore "ExpenseManagement.csproj"

# Copy source code
COPY . .

# Build
RUN dotnet build "ExpenseManagement.csproj" -c Release -o /app/build

# Publish
FROM build AS publish
RUN dotnet publish "ExpenseManagement.csproj" -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine
WORKDIR /app

# Install curl for health checks
RUN apk add --no-cache curl

# Copy published files
COPY --from=publish /app/publish .

# Create non-root user for security
RUN addgroup -g 1000 appuser && \
    adduser -D -u 1000 -G appuser appuser && \
    chown -R appuser:appuser /app

USER appuser

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

EXPOSE 5000
ENTRYPOINT ["dotnet", "ExpenseManagement.dll"]
```

### docker-compose.yml (Production-Ready)

```yaml
version: '3.8'

services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2019-latest
    container_name: expensemanagement-db
    environment:
      ACCEPT_EULA: Y
      SA_PASSWORD: YourStrongPassword123!@#
      MSSQL_PID: Express
    ports:
      - "1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql
      - sqlserver_backup:/var/opt/mssql/backup
    networks:
      - expensemanagement-net
    restart: unless-stopped
    healthcheck:
      test: /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'YourStrongPassword123!@#' -Q "SELECT 1" || exit 1
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 20s

  app:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: expensemanagement-app
    depends_on:
      sqlserver:
        condition: service_healthy
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:5000
      ConnectionStrings__DefaultConnection: "Server=sqlserver;Database=ExpenseManagementDB;User Id=sa;Password=YourStrongPassword123!@#;TrustServerCertificate=True;Connection Timeout=30;"
    ports:
      - "5000:5000"
    networks:
      - expensemanagement-net
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:5000/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "3"

volumes:
  sqlserver_data:
    driver: local
  sqlserver_backup:
    driver: local

networks:
  expensemanagement-net:
    driver: bridge
```

## Nginx Reverse Proxy Configuration

```nginx
# /etc/nginx/sites-available/expensemanagement

upstream dotnet_backend {
    server localhost:5000;
    keepalive 32;
}

server {
    listen 80;
    server_name yourdomain.com www.yourdomain.com;

    # Redirect to HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name yourdomain.com www.yourdomain.com;

    # SSL Configuration
    ssl_certificate /etc/ssl/certs/your_cert.crt;
    ssl_certificate_key /etc/ssl/private/your_key.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    # Security Headers
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-Frame-Options "DENY" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    # Compression
    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;
    gzip_min_length 1000;

    # Logging
    access_log /var/log/nginx/expensemanagement_access.log;
    error_log /var/log/nginx/expensemanagement_error.log;

    # Proxy Settings
    location / {
        proxy_pass http://dotnet_backend;
        proxy_http_version 1.1;

        # Headers
        proxy_set_header Connection "";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Port $server_port;

        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }

    # Static files caching
    location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$ {
        expires 30d;
        add_header Cache-Control "public, immutable";
        proxy_pass http://dotnet_backend;
    }
}
```

## Azure App Service Configuration

### Application Settings

```
Key: ASPNETCORE_ENVIRONMENT
Value: Production

Key: ConnectionStrings__DefaultConnection
Value: Server=tcp:expensemanagement-server.database.windows.net,1433;Initial Catalog=ExpenseManagementDB;Persist Security Info=False;User ID=sqladmin;Password=YourStrongPassword123!@#;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;

Key: WEBSITES_ENABLE_APP_SERVICE_STORAGE
Value: true

Key: ApplicationInsightsAgent_EXTENSION_VERSION
Value: ~3
```

### Connection Strings

```
Name: DefaultConnection
Value: Server=tcp:expensemanagement-server.database.windows.net,1433;Initial Catalog=ExpenseManagementDB;Persist Security Info=False;User ID=sqladmin;Password=YourStrongPassword123!@#;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
Type: SQLServer
```

## SQL Server Configuration

### Login & Permissions

```sql
-- Create SQL login
CREATE LOGIN produser WITH PASSWORD = 'YourStrongPassword123!@#';

-- Create database user
USE ExpenseManagementDB;
CREATE USER produser FOR LOGIN produser;

-- Grant permissions
ALTER ROLE db_owner ADD MEMBER produser;

-- For Application Insights (if using)
CREATE ROLE db_diagnostic_collection;
GRANT SELECT ON sys.dm_exec_requests TO db_diagnostic_collection;
GRANT SELECT ON sys.database_files TO db_diagnostic_collection;
```

### Database Maintenance

```sql
-- Create maintenance plan for backups
-- Full backup daily
-- Transaction log backup every 2 hours
-- Index optimization weekly
-- Statistics update weekly

-- Backup configuration
BACKUP DATABASE ExpenseManagementDB 
TO DISK = 'S:\Backups\ExpenseManagementDB_FULL_$(DATE).bak'
WITH INIT, COMPRESSION, STATS = 10;

-- Index maintenance
ALTER INDEX ALL ON [dbo].[Users] REBUILD;
ALTER INDEX ALL ON [dbo].[Budgets] REORGANIZE;
ALTER INDEX ALL ON [dbo].[ExpenseReports] REBUILD;
```

## Performance Tuning

### Application Code Changes (if needed)

```csharp
// Program.cs - Production optimizations
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions => 
        {
            sqlServerOptions.CommandTimeout(30);
            sqlServerOptions.MaxBatchSize(1000);
        })
    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)); // For read-only operations

// Enable response compression
builder.Services.AddResponseCompression();
app.UseResponseCompression();

// Add security middleware
app.UseHsts();
app.UseHttpsRedirection();
```

### SQL Server Indexes (Optional - if performance issues)

```sql
-- Add indexes for frequently queried columns
CREATE INDEX IX_ExpenseReports_EmployeeId_Status 
ON ExpenseReports(EmployeeId, Status) 
INCLUDE (CreatedOn, TotalClaimed);

CREATE INDEX IX_ExpenseClaims_ReportId 
ON ExpenseClaims(ExpenseReportId) 
INCLUDE (Amount, Status);

CREATE INDEX IX_Budgets_EmployeeId_IsActive 
ON Budgets(EmployeeId, IsActive) 
INCLUDE (RemainingBalance);
```

---

## Verification Commands

```bash
# Test connectivity
sqlcmd -S YOUR_SERVER -U produser -P "YourStrongPassword123!@#" -d ExpenseManagementDB -Q "SELECT 1"

# Docker health check
docker inspect --format='{{.State.Health.Status}}' expensemanagement-app

# Application endpoints
curl -k https://yourdomain.com/health
curl -k https://yourdomain.com/Account/Login

# Check logs
docker logs expensemanagement-app
tail -f /var/log/nginx/expensemanagement_access.log  # Nginx

# Database backup verification
RESTORE VERIFYONLY FROM DISK = 'S:\Backups\ExpenseManagementDB_FULL_2024.bak'
```

---

**Last Updated**: 2024  
**Version**: 1.0  
**Environment**: Production
