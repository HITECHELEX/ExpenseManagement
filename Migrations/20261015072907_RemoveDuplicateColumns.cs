using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDuplicateColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- PAN / UAN / ESIC: one copy, on the employee ----
            migrationBuilder.AddColumn<string>(
                name: "UANNumber", table: "Employees",
                type: "character varying(30)", maxLength: 30, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ESICNumber", table: "Employees",
                type: "character varying(30)", maxLength: 30, nullable: true);

            // Keep the data. The profile PAN wins when both exist; UAN and ESIC
            // take the latest non-empty value from the salary revisions.
            migrationBuilder.Sql(@"
UPDATE ""Employees"" e SET
  ""PANNumber"" = COALESCE(NULLIF(e.""PANNumber"", ''), (
      SELECT s.""PANNumber"" FROM ""SalaryMasters"" s
      WHERE s.""EmployeeId"" = e.""Id"" AND COALESCE(s.""PANNumber"", '') <> ''
      ORDER BY s.""EffectiveFrom"" DESC LIMIT 1)),
  ""UANNumber"" = (
      SELECT s.""UANNumber"" FROM ""SalaryMasters"" s
      WHERE s.""EmployeeId"" = e.""Id"" AND COALESCE(s.""UANNumber"", '') <> ''
      ORDER BY s.""EffectiveFrom"" DESC LIMIT 1),
  ""ESICNumber"" = (
      SELECT s.""ESICNumber"" FROM ""SalaryMasters"" s
      WHERE s.""EmployeeId"" = e.""Id"" AND COALESCE(s.""ESICNumber"", '') <> ''
      ORDER BY s.""EffectiveFrom"" DESC LIMIT 1)
WHERE EXISTS (SELECT 1 FROM ""SalaryMasters"" s WHERE s.""EmployeeId"" = e.""Id"");");

            migrationBuilder.DropColumn(name: "PANNumber", table: "SalaryMasters");
            migrationBuilder.DropColumn(name: "UANNumber", table: "SalaryMasters");
            migrationBuilder.DropColumn(name: "ESICNumber", table: "SalaryMasters");

            // ---- Payrolls: duplicate columns ----
            migrationBuilder.Sql(@"
UPDATE ""Payrolls"" SET ""PaidDays"" = ""PayDays""
WHERE ""PaidDays"" IS NULL AND ""PayDays"" IS NOT NULL;");

            migrationBuilder.Sql(@"
UPDATE ""Payrolls"" SET ""PaymentDate"" = ""PaidOn""
WHERE ""PaymentDate"" IS NULL AND ""PaidOn"" IS NOT NULL;");

            migrationBuilder.DropColumn(name: "PayDays", table: "Payrolls");
            migrationBuilder.DropColumn(name: "PaidOn", table: "Payrolls");
            migrationBuilder.DropColumn(name: "PayrollPeriodFrom", table: "Payrolls");
            migrationBuilder.DropColumn(name: "PayrollPeriodTo", table: "Payrolls");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ---- Payrolls ----
            migrationBuilder.AddColumn<decimal>(
                name: "PayDays", table: "Payrolls",
                type: "numeric(5,2)", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidOn", table: "Payrolls",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayrollPeriodFrom", table: "Payrolls",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PayrollPeriodTo", table: "Payrolls",
                type: "timestamp with time zone", nullable: true);

            migrationBuilder.Sql(@"
UPDATE ""Payrolls"" SET ""PayDays"" = ""PaidDays"", ""PaidOn"" = ""PaymentDate"";");

            // ---- Statutory numbers back to the salary revisions ----
            migrationBuilder.AddColumn<string>(
                name: "PANNumber", table: "SalaryMasters",
                type: "character varying(20)", maxLength: 20, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UANNumber", table: "SalaryMasters",
                type: "character varying(30)", maxLength: 30, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ESICNumber", table: "SalaryMasters",
                type: "character varying(30)", maxLength: 30, nullable: true);

            migrationBuilder.Sql(@"
UPDATE ""SalaryMasters"" s SET
  ""PANNumber"" = e.""PANNumber"",
  ""UANNumber"" = e.""UANNumber"",
  ""ESICNumber"" = e.""ESICNumber""
FROM ""Employees"" e WHERE e.""Id"" = s.""EmployeeId"";");

            migrationBuilder.DropColumn(name: "UANNumber", table: "Employees");
            migrationBuilder.DropColumn(name: "ESICNumber", table: "Employees");
        }
    }
}