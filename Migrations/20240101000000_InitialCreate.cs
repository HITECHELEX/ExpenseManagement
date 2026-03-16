using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Department = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Budgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    AllocatedById = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SpentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReturnedBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AllocatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Budgets", x => x.Id);
                    table.ForeignKey(name: "FK_Budgets_Users_AllocatedById", column: x => x.AllocatedById, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_Budgets_Users_EmployeeId", column: x => x.EmployeeId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    ReportTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TotalClaimed = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalApproved = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingBalanceAtSubmission = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AccountTeamNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManagementNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ManagementFeedback = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AccountTeamVerifiedById = table.Column<int>(type: "int", nullable: true),
                    AccountTeamVerifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ManagementReviewedById = table.Column<int>(type: "int", nullable: true),
                    ManagementReviewedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ForwardedToAccountTeamOn = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseReports", x => x.Id);
                    table.ForeignKey(name: "FK_ExpenseReports_Budgets_BudgetId", column: x => x.BudgetId, principalTable: "Budgets", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExpenseReports_Users_AccountTeamVerifiedById", column: x => x.AccountTeamVerifiedById, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExpenseReports_Users_EmployeeId", column: x => x.EmployeeId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExpenseReports_Users_ManagementReviewedById", column: x => x.ManagementReviewedById, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    BudgetId = table.Column<int>(type: "int", nullable: false),
                    ExpenseReportId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ReceiptPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReceiptFileName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AccountTeamRemarks = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedById = table.Column<int>(type: "int", nullable: true),
                    ReviewedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpenseDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseClaims", x => x.Id);
                    table.ForeignKey(name: "FK_ExpenseClaims_Budgets_BudgetId", column: x => x.BudgetId, principalTable: "Budgets", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExpenseClaims_ExpenseReports_ExpenseReportId", column: x => x.ExpenseReportId, principalTable: "ExpenseReports", principalColumn: "Id");
                    table.ForeignKey(name: "FK_ExpenseClaims_Users_EmployeeId", column: x => x.EmployeeId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "FK_ExpenseClaims_Users_ReviewedById", column: x => x.ReviewedById, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Link = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Icon = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(name: "FK_Notifications_Users_UserId", column: x => x.UserId, principalTable: "Users", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                });

            // Seed data
            migrationBuilder.InsertData("Users",
                new[] { "Id", "FullName", "Email", "PasswordHash", "Role", "Department", "CreatedAt" },
                new object[] { 1, "Admin Management", "management@company.com", BCrypt.Net.BCrypt.HashPassword("Management@123"), 2, "Management", new DateTime(2024, 1, 1) });
            migrationBuilder.InsertData("Users",
                new[] { "Id", "FullName", "Email", "PasswordHash", "Role", "Department", "CreatedAt" },
                new object[] { 2, "Accounts Head", "accounts@company.com", BCrypt.Net.BCrypt.HashPassword("Accounts@123"), 1, "Finance", new DateTime(2024, 1, 1) });
            migrationBuilder.InsertData("Users",
                new[] { "Id", "FullName", "Email", "PasswordHash", "Role", "Department", "CreatedAt" },
                new object[] { 3, "John Employee", "employee@company.com", BCrypt.Net.BCrypt.HashPassword("Employee@123"), 0, "Sales", new DateTime(2024, 1, 1) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Notifications");
            migrationBuilder.DropTable(name: "ExpenseClaims");
            migrationBuilder.DropTable(name: "ExpenseReports");
            migrationBuilder.DropTable(name: "Budgets");
            migrationBuilder.DropTable(name: "Users");
        }
    }
}
