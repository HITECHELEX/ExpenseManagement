using ExpenseManagement.Data;
using ExpenseManagement.Models;
using ExpenseManagement.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    options.UseNpgsql(connectionString);
});
builder.Services.AddScoped<S3Service>();
builder.Services.AddScoped<LeavePolicyService>();
builder.Services.AddSingleton<AttendanceProcessingService>();

builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<AttendanceProcessingService>());
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.Events.OnValidatePrincipal = async context =>
        {
            var principal = context.Principal;
            if (principal == null || !principal.IsInRole("Employee")) return;

            if (!int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
                return;

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();

            var emp = await db.Employees
                .AsNoTracking()
                .Where(e => e.UserId == userId)
                .Select(e => new { e.EmploymentStatus, e.ExitDate })
                .FirstOrDefaultAsync();

            var todayIst = DateTime.UtcNow.AddHours(5).AddMinutes(30).Date;

            var blocked = emp != null && (
                string.Equals(emp.EmploymentStatus, "Inactive", StringComparison.OrdinalIgnoreCase) ||
                (emp.ExitDate.HasValue && emp.ExitDate.Value.Date < todayIst));

            if (blocked)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Database initialization
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (app.Environment.IsDevelopment())
        {
            db.Database.Migrate();

            logger.LogInformation(
                "AppDbContext: Development database migrations completed."
            );

            // ---------------------------------------------------------
            // DEVELOPMENT HR BOOTSTRAP
            // Creates HR login accounts and HR profiles only if missing.
            // This code NEVER runs in Production.
            // ---------------------------------------------------------

            // HITECH HR USER
            var hitechHr = db.Users
                .FirstOrDefault(u => u.Email == "hr.hitech@company.com");

            if (hitechHr == null)
            {
                hitechHr = new User
                {
                    FullName = "Hitech HR",
                    Email = "hr.hitech@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("HitechHR@123"),
                    Role = UserRole.HR,
                    Department = "Human Resources",
                    CompanyId = 1,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(hitechHr);

                logger.LogInformation(
                    "Development HR user created: Hitech HR"
                );
            }

            // CEYONE HR USER
            var ceyoneHr = db.Users
                .FirstOrDefault(u => u.Email == "hr.ceyone@company.com");

            if (ceyoneHr == null)
            {
                ceyoneHr = new User
                {
                    FullName = "Ceyone HR",
                    Email = "hr.ceyone@company.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("CeyoneHR@123"),
                    Role = UserRole.HR,
                    Department = "Human Resources",
                    CompanyId = 2,
                    CreatedAt = DateTime.UtcNow
                };

                db.Users.Add(ceyoneHr);

                logger.LogInformation(
                    "Development HR user created: Ceyone HR"
                );
            }

            // HITECH HR PROFILE
            var hitechHrProfile = db.PortalProfiles
                .FirstOrDefault(p =>
                    p.Role == UserRole.HR &&
                    p.CompanyId == 1 &&
                    p.Name == "Hitech HR");

            if (hitechHrProfile == null)
            {
                db.PortalProfiles.Add(new PortalProfile
                {
                    Name = "Hitech HR",
                    Role = UserRole.HR,
                    CompanyId = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                logger.LogInformation(
                    "Development HR profile created: Hitech HR"
                );
            }

            // CEYONE HR PROFILE
            var ceyoneHrProfile = db.PortalProfiles
                .FirstOrDefault(p =>
                    p.Role == UserRole.HR &&
                    p.CompanyId == 2 &&
                    p.Name == "Ceyone HR");

            if (ceyoneHrProfile == null)
            {
                db.PortalProfiles.Add(new PortalProfile
                {
                    Name = "Ceyone HR",
                    Role = UserRole.HR,
                    CompanyId = 2,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                logger.LogInformation(
                    "Development HR profile created: Ceyone HR"
                );
            }

            db.SaveChanges();

            logger.LogInformation(
                "Development HR bootstrap completed."
            );
        }
        else
        {
            db.Database.Migrate();

            logger.LogInformation(
                "AppDbContext: Production migrations completed."
            );
        }
    }
    catch (Exception ex)
    {
        logger.LogError(
            ex,
            "AppDbContext: Database initialization failed."
        );

        throw;
    }
}

app.MapControllerRoute(name: "default", pattern: "{controller=Account}/{action=Login}/{id?}");
app.Run();
