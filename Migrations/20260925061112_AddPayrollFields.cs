using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            
            migrationBuilder.AddColumn<decimal>(
                name: "LOP",
                table: "Payrolls",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LWF",
                table: "Payrolls",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PayDays",
                table: "Payrolls",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayslipNumber",
                table: "Payrolls",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WashingAllowance",
                table: "Payrolls",
                type: "numeric(12,2)",
                nullable: true);

            
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropColumn(
                name: "LOP",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "LWF",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PayDays",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "PayslipNumber",
                table: "Payrolls");

            migrationBuilder.DropColumn(
                name: "WashingAllowance",
                table: "Payrolls");
        }
    }
}
