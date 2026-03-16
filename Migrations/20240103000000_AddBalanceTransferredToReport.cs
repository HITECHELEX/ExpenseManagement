using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddBalanceTransferredToReport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='BalanceTransferred'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] ADD [BalanceTransferred] bit NOT NULL DEFAULT 0
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='BalanceTransferredOn'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] ADD [BalanceTransferredOn] datetime2 NULL
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='BalanceTransferred'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] DROP COLUMN [BalanceTransferred]
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='BalanceTransferredOn'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] DROP COLUMN [BalanceTransferredOn]
                END
            ");
        }
    }
}
