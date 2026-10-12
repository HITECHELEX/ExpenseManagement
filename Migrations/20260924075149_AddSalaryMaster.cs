using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddSalaryMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
          

            migrationBuilder.CreateTable(
                name: "SalaryMasters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    PANNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    UANNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    ESICNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    BasicSalary = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    HRA = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    ConveyanceAllowance = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    WashingAllowance = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    OtherAllowances = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    PFApplicable = table.Column<bool>(type: "boolean", nullable: false),
                    ESICApplicable = table.Column<bool>(type: "boolean", nullable: false),
                    ProfessionalTaxApplicable = table.Column<bool>(type: "boolean", nullable: false),
                    LWFApplicable = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryMasters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryMasters_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryMasters_EmployeeId",
                table: "SalaryMasters",
                column: "EmployeeId",
                unique: true,
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalaryMasters");

            
        }
    }
}
