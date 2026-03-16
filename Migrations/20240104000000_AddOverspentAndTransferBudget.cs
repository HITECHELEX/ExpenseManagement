using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    public partial class AddOverspentAndTransferBudget : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Budgets: OverspentAmount
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='OverspentAmount'
                )
                BEGIN
                    ALTER TABLE [Budgets] ADD [OverspentAmount] decimal(18,2) NOT NULL DEFAULT 0
                END
            ");

            // ExpenseReports: TransferredToBudgetId
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='OverspentAmount')
                    ALTER TABLE [Budgets] DROP COLUMN [OverspentAmount]
            ");
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='TransferredToBudgetId')
                    ALTER TABLE [ExpenseReports] DROP COLUMN [TransferredToBudgetId]
            ");
        }
    }
}
