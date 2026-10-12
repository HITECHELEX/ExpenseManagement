# ExpenseManagement - Deployment Checklist

## Pre-Flight Checklist (Before Any Deployment)

### Code Quality
- [ ] All changes committed to Git (master branch)
- [ ] Code reviewed and approved
- [ ] `git log` shows recent commits
- [ ] No uncommitted changes: `git status` shows clean working directory
- [ ] Latest branch pulled: `git pull origin master`

### Build & Tests
- [ ] Clean build successful: `dotnet clean && dotnet build`
- [ ] No compiler warnings (treat as errors if configured)
- [ ] Unit tests pass: `dotnet test`
- [ ] Integration tests pass
- [ ] Project builds in Release mode: `dotnet build -c Release`

### Database Preparation
- [ ] Database backup taken
- [ ] Migration script tested on staging
- [ ] Backup restoration tested (point-in-time recovery works)
- [ ] Database user/credentials secured (not in git)
- [ ] Connection string updated for target environment

### Configuration Files
- [ ] `appsettings.json` reviewed for target environment
- [ ] `appsettings.Production.json` prepared (if applicable)
- [ ] All secrets moved to secure storage (Key Vault, env vars)
- [ ] Logging levels set appropriately
- [ ] AllowedHosts configured correctly

### Security
- [ ] SSL/TLS certificate valid and not expired
- [ ] Certificate installed in target environment
- [ ] HTTPS enforced in code
- [ ] HSTS headers enabled
- [ ] Authentication/Authorization policies verified
- [ ] CORS settings reviewed
- [ ] No hardcoded passwords in code/config

### Infrastructure
- [ ] Target server/container has minimum resources:
  - [ ] .NET 8 Runtime installed (ASP.NET Core Hosting Bundle for IIS)
  - [ ] SQL Server 2019+ running and accessible
  - [ ] Disk space available (minimum 5 GB free)
  - [ ] RAM adequate (minimum 4 GB, 8 GB recommended)
- [ ] Network connectivity tested to database
- [ ] Firewall rules configured (ports 80, 443 open)
- [ ] DNS/Domain name resolves correctly

### Monitoring & Logging
- [ ] Application Insights / Serilog configured
- [ ] Log aggregation service available
- [ ] Error alerting configured
- [ ] Performance monitoring dashboards ready
- [ ] Support team trained on monitoring tools

---

## Windows Server + IIS Deployment

### Pre-Deployment
- [ ] IIS installed and running
- [ ] .NET 8 Hosting Bundle installed
- [ ] Application Pool created with "No Managed Code"
- [ ] Website binding configured (HTTP & HTTPS)
- [ ] SSL certificate installed and bound
- [ ] Directory permissions set for IIS AppPool user
- [ ] App pool identity has read/write to app directory

### During Deployment
- [ ] Publish Release build: `dotnet publish -c Release -o C:\path\to\deploy`
- [ ] Copy published files to IIS root
- [ ] Copy environment-specific `appsettings.json`
- [ ] Verify all dependencies present in bin/Release
- [ ] Restart IIS: `iisreset /restart`
- [ ] Monitor Event Viewer for startup errors

### Post-Deployment
- [ ] Application starts without errors
- [ ] Navigate to `https://yourdomain.com` → Login page appears
- [ ] Test login with test user credentials
- [ ] Test basic workflows (create budget, submit report)
- [ ] Database connection verified (query executes)
- [ ] Check IIS logs: `C:\inetpub\logs\LogFiles`
- [ ] Check Application Event Viewer for errors
- [ ] Monitor for 30 minutes for runtime errors

---

## Docker Deployment

### Pre-Deployment
- [ ] Docker Desktop installed and running
- [ ] Docker image builds successfully: `docker build -t expensemanagement:latest .`
- [ ] Image size reasonable (~500 MB)
- [ ] Base images (SDK & runtime) current: `docker pull mcr.microsoft.com/dotnet/aspnet:8.0`
- [ ] SQL Server container image available or external DB ready

### During Deployment
- [ ] Stop running container (if exists): `docker stop expensemanagement-app`
- [ ] Remove old container: `docker rm expensemanagement-app`
- [ ] Tag image: `docker tag expensemanagement:latest expensemanagement:v1.0`
- [ ] Run new container with correct environment variables
- [ ] Verify container startup: `docker logs -f expensemanagement-app`
- [ ] Wait 10-15 seconds for migrations to complete

### Post-Deployment
- [ ] Container running: `docker ps | grep expensemanagement`
- [ ] Container hasn't restarted: `docker ps -a` shows no "Exited" status
- [ ] Port mapping correct: `curl http://localhost:5000`
- [ ] Application responds (HTML login page)
- [ ] Test login functionality
- [ ] Monitor logs for errors: `docker logs -f expensemanagement-app`
- [ ] No out-of-memory errors
- [ ] Container resource limits appropriate

---

## Azure Deployment

### Pre-Deployment
- [ ] Azure subscription active and accessible
- [ ] Resource group created
- [ ] App Service Plan created with appropriate SKU
- [ ] Azure SQL Server provisioned
- [ ] Azure SQL Database created
- [ ] Firewall rules allow connection from app
- [ ] Connection string configured in App Service settings
- [ ] Application Insights resource created (optional but recommended)

### During Deployment
- [ ] Publish to Azure: `dotnet publish -c Release`
- [ ] Create deployment package (ZIP or Git)
- [ ] Upload via Azure Portal or CLI
- [ ] Monitor deployment progress
- [ ] Database migrations auto-run (configured in Program.cs)
- [ ] Application settings applied

### Post-Deployment
- [ ] App Service started successfully
- [ ] Navigate to `https://appname.azurewebsites.net`
- [ ] Login page loads
- [ ] Test authentication
- [ ] Database queries execute
- [ ] View logs in Azure Portal or Application Insights
- [ ] Monitor resource utilization
- [ ] Test from multiple geographic locations (latency check)

---

## Database Deployment Checklist

### Before Running Migrations
- [ ] Backup current database
- [ ] Document current schema version
- [ ] Review migration code for breaking changes
- [ ] Test migrations on staging environment
- [ ] Verify rollback procedure

### Running Migrations
- [ ] Connection string verified
- [ ] Migration user has appropriate permissions
- [ ] Run migration: `dotnet ef database update`
- [ ] Monitor migration progress (large tables may take time)
- [ ] Check for errors in application startup logs

### Post-Migration Verification
- [ ] New tables/columns exist: Query sys.tables
- [ ] Data migrated correctly
- [ ] Indexes created: `SELECT * FROM sys.indexes`
- [ ] Foreign key constraints intact
- [ ] No orphaned data
- [ ] Database integrity check: `DBCC CHECKDB`
- [ ] Application functionality tested with new schema

---

## Rollback Checklist (If Needed)

### Immediate Actions
- [ ] Document incident details and error logs
- [ ] Notify stakeholders of issue
- [ ] Take application offline (if severe)
- [ ] Preserve error logs for analysis

### Application Rollback
- [ ] Stop current deployment
- [ ] Restore previous application files
- [ ] Restart application/container
- [ ] Verify application starts
- [ ] Test basic functionality

### Database Rollback
- [ ] Restore database backup: `RESTORE DATABASE...`
- [ ] Verify data integrity
- [ ] Run migration rollback if needed: `dotnet ef database update PreviousMigrationName`
- [ ] Test with previous application version

### Communication
- [ ] Notify users of rollback
- [ ] Provide incident summary
- [ ] Schedule post-mortem review
- [ ] Plan fix for next deployment

---

## 30-Day Post-Deployment Monitoring

### Week 1
- [ ] Daily log review for errors
- [ ] Monitor application performance metrics
- [ ] User feedback collection
- [ ] Database growth monitoring
- [ ] Backup validation (restore test)

### Week 2-3
- [ ] Weekly security scan
- [ ] Review slow query logs
- [ ] Check disk space utilization
- [ ] Verify automated backups running
- [ ] Performance baseline established

### Week 4
- [ ] Monthly metrics review
- [ ] User adoption tracking
- [ ] Capacity planning analysis
- [ ] Update runbook based on learnings
- [ ] Go-live sign-off and closure

---

## Emergency Contacts

| Role | Name | Phone | Email |
|------|------|-------|-------|
| **System Administrator** | | | |
| **DBA** | | | |
| **DevOps Engineer** | | | |
| **Application Owner** | | | |
| **Escalation Lead** | | | |

---

## Deployment Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| **Deployment Lead** | | | |
| **QA Lead** | | | |
| **Operations Manager** | | | |
| **Project Manager** | | | |

---

## Deployment Runbook Quick Links

- [Full Deployment Guide](./DEPLOYMENT_GUIDE.md)
- [Troubleshooting Guide](#troubleshooting)
- [Rollback Procedures](#rollback-checklist)
- [GitHub Repository](https://github.com/HITECHELEX/ExpenseManagement)
- [Azure Portal](https://portal.azure.com)
- [Docker Hub](https://hub.docker.com)

---

**Last Updated**: 2024  
**Version**: 1.0  
**Review Frequency**: Quarterly
