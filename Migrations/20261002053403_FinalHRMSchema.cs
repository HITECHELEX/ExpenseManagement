using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class FinalHRMSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankFileReference",
                table: "Payrolls",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerESIC",
                table: "Payrolls",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerLWF",
                table: "Payrolls",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerOtherContribution",
                table: "Payrolls",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerPF",
                table: "Payrolls",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidOn",
                table: "Payrolls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDate",
                table: "Payrolls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentReference",
                table: "Payrolls",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Payrolls",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayrollPeriodFrom",
                table: "Payrolls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayrollPeriodTo",
                table: "Payrolls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PerformanceBonus",
                table: "Payrolls",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AadhaarNumber",
                table: "Employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountHolderName",
                table: "Employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "Employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankBranch",
                table: "Employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankIFSCCode",
                table: "Employees",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "Employees",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsIDBI",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsNonIDBI",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnSite",
                table: "Employees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MedicalHistory",
                table: "Employees",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingCompletedOn",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnboardingStartedOn",
                table: "Employees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnboardingStatus",
                table: "Employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PANNumber",
                table: "Employees",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BiometricImportReference",
                table: "Attendances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BiometricPunchesJson",
                table: "Attendances",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CheckInAccuracy",
                table: "Attendances",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CheckInLatitude",
                table: "Attendances",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckInLocationName",
                table: "Attendances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CheckInLongitude",
                table: "Attendances",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CheckOutAccuracy",
                table: "Attendances",
                type: "numeric(8,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CheckOutLatitude",
                table: "Attendances",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckOutLocationName",
                table: "Attendances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CheckOutLongitude",
                table: "Attendances",
                type: "numeric(9,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShiftCode",
                table: "Attendances",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    AssetCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AssetName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AssetType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SerialNumber = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CurrentEmployeeId = table.Column<int>(type: "integer", nullable: true),
                    AssignedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpectedReturnDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ConditionOnAssignment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ConditionOnReturn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Assets_Employees_CurrentEmployeeId",
                        column: x => x.CurrentEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: true),
                    DocumentKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DocumentCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DocumentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: true),
                    TemplateContent = table.Column<string>(type: "text", nullable: true),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FilePath = table.Column<string>(type: "text", nullable: true),
                    DocumentStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsOnboardingDocument = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresSignature = table.Column<bool>(type: "boolean", nullable: false),
                    SignatureStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SignedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignatureReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    UploadedBy = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Users_UploadedBy",
                        column: x => x.UploadedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HRMConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ConfigurationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HRMConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HRMConfigurations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AssetId = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<int>(type: "integer", nullable: true),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ActionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Condition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    PerformedBy = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssetHistories_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetHistories_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetHistories_Users_PerformedBy",
                        column: x => x.PerformedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_ShiftCode",
                table: "Attendances",
                column: "ShiftCode");

            migrationBuilder.CreateIndex(
                name: "IX_AssetHistories_AssetId_ActionDate",
                table: "AssetHistories",
                columns: new[] { "AssetId", "ActionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetHistories_EmployeeId",
                table: "AssetHistories",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AssetHistories_PerformedBy",
                table: "AssetHistories",
                column: "PerformedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_CompanyId_AssetCode",
                table: "Assets",
                columns: new[] { "CompanyId", "AssetCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_CurrentEmployeeId",
                table: "Assets",
                column: "CurrentEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CompanyId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "CompanyId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CompanyId_DocumentKind",
                table: "Documents",
                columns: new[] { "CompanyId", "DocumentKind" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CompanyId_EmployeeId",
                table: "Documents",
                columns: new[] { "CompanyId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_EmployeeId",
                table: "Documents",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_UploadedBy",
                table: "Documents",
                column: "UploadedBy");

            migrationBuilder.CreateIndex(
                name: "IX_HRMConfigurations_CompanyId_ConfigurationType_Configuratio~1",
                table: "HRMConfigurations",
                columns: new[] { "CompanyId", "ConfigurationType", "ConfigurationCode", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HRMConfigurations_CompanyId_ConfigurationType_Configuration~",
                table: "HRMConfigurations",
                columns: new[] { "CompanyId", "ConfigurationType", "ConfigurationCode", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetHistories");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "HRMConfigurations");

            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_ShiftCode",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "BankFileReference",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "EmployerESIC",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "EmployerLWF",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "EmployerOtherContribution",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "EmployerPF",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PaidOn",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PaymentDate",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PaymentReference",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PayrollPeriodFrom",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PayrollPeriodTo",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PerformanceBonus",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "AadhaarNumber",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankAccountHolderName",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankBranch",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankIFSCCode",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IsIDBI",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IsNonIDBI",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "IsOnSite",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "MedicalHistory",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "OnboardingCompletedOn",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "OnboardingStartedOn",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "OnboardingStatus",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "PANNumber",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "BiometricImportReference",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "BiometricPunchesJson",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckInAccuracy",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckInLatitude",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckInLocationName",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckInLongitude",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckOutAccuracy",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckOutLatitude",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckOutLocationName",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "CheckOutLongitude",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "ShiftCode",
                table: "Attendances");
        }
    }
}
