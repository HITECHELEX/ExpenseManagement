using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddOverspentAndTransferredToBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='OverspentAmount'
                )
                BEGIN
                    ALTER TABLE [Budgets] ADD [OverspentAmount] decimal(18,2) NOT NULL DEFAULT 0
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='TransferredToBudgetId'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] ADD [TransferredToBudgetId] int NULL
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='OverspentAmount'
                )
                BEGIN
                    ALTER TABLE [Budgets] DROP COLUMN [OverspentAmount]
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='TransferredToBudgetId'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] DROP COLUMN [TransferredToBudgetId]
                END
            ");
        }
    }
}
