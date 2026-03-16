using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    public partial class AddBudgetLocking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='IsLocked'
                )
                BEGIN
                    ALTER TABLE [Budgets] ADD [IsLocked] bit NOT NULL DEFAULT 0
                END
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='LockedReason'
                )
                BEGIN
                    ALTER TABLE [Budgets] ADD [LockedReason] nvarchar(500) NULL
                END
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='IsLocked') ALTER TABLE [Budgets] DROP COLUMN [IsLocked]");
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='LockedReason') ALTER TABLE [Budgets] DROP COLUMN [LockedReason]");
        }
    }
}
