using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddNewColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ExpenseClaims: add ExpenseDate ────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseClaims' AND COLUMN_NAME='ExpenseDate'
                )
                BEGIN
                    ALTER TABLE [ExpenseClaims] ADD [ExpenseDate] datetime2 NOT NULL DEFAULT GETDATE()
                END
            ");

            // ── ExpenseClaims: add ReceiptFileName ────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseClaims' AND COLUMN_NAME='ReceiptFileName'
                )
                BEGIN
                    ALTER TABLE [ExpenseClaims] ADD [ReceiptFileName] nvarchar(max) NULL
                END
            ");

            // ── Budgets: add ReturnedBalance ──────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='ReturnedBalance'
                )
                BEGIN
                    ALTER TABLE [Budgets] ADD [ReturnedBalance] decimal(18,2) NOT NULL DEFAULT 0
                END
            ");

            // ── ExpenseReports: add RemainingBalanceAtSubmission ──
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='RemainingBalanceAtSubmission'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] ADD [RemainingBalanceAtSubmission] decimal(18,2) NOT NULL DEFAULT 0
                END
            ");

            // ── ExpenseReports: add ManagementFeedback ────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='ManagementFeedback'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] ADD [ManagementFeedback] nvarchar(max) NULL
                END
            ");

            // ── ExpenseReports: add ForwardedToAccountTeamOn ──────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='ForwardedToAccountTeamOn'
                )
                BEGIN
                    ALTER TABLE [ExpenseReports] ADD [ForwardedToAccountTeamOn] datetime2 NULL
                END
            ");

            // ── ReportStatus: update old enum values ──────────────
            // Old: Draft=0, SubmittedToAccountTeam=1, VerifiedByAccountTeam=2, ForwardedToManagement=3, Closed=4
            // New: Draft=0, SubmittedToAccountTeam=1, ReviewedByAccountTeam=2, ForwardedToManagement=3, ApprovedByManagement=4, RejectedByManagement=5
            // Closed(4) -> ApprovedByManagement(4): same int value, so no update needed for Closed reports
            // VerifiedByAccountTeam(2) -> ReviewedByAccountTeam(2): same int, no change needed

            // ── Notifications table ───────────────────────────────
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_NAME='Notifications'
                )
                BEGIN
                    CREATE TABLE [Notifications] (
                        [Id] int NOT NULL IDENTITY(1,1),
                        [UserId] int NOT NULL,
                        [Message] nvarchar(max) NOT NULL,
                        [Link] nvarchar(max) NULL,
                        [Icon] nvarchar(max) NOT NULL DEFAULT 'bell',
                        [IsRead] bit NOT NULL DEFAULT 0,
                        [CreatedAt] datetime2 NOT NULL DEFAULT GETDATE(),
                        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_Notifications_Users_UserId] FOREIGN KEY ([UserId])
                            REFERENCES [Users] ([Id]) ON DELETE CASCADE
                    )
                END
            ");

            // ── ExpenseReports: fix Status column if needed ───────
            // Update old "VerifiedByAccountTeam" status name - int value stays same (2)
            // Update old "Closed" (4) -> now maps to ApprovedByManagement (4) - same int, fine

            // ── ExpenseClaims: ensure ExpenseReportId FK allows null (it should already) ─
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME='ExpenseClaims' AND COLUMN_NAME='ExpenseReportId'
                )
                BEGIN
                    ALTER TABLE [ExpenseClaims] ADD [ExpenseReportId] int NULL
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ExpenseClaims' AND COLUMN_NAME='ExpenseDate') ALTER TABLE [ExpenseClaims] DROP COLUMN [ExpenseDate]");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ExpenseClaims' AND COLUMN_NAME='ReceiptFileName') ALTER TABLE [ExpenseClaims] DROP COLUMN [ReceiptFileName]");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='Budgets' AND COLUMN_NAME='ReturnedBalance') ALTER TABLE [Budgets] DROP COLUMN [ReturnedBalance]");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='RemainingBalanceAtSubmission') ALTER TABLE [ExpenseReports] DROP COLUMN [RemainingBalanceAtSubmission]");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='ManagementFeedback') ALTER TABLE [ExpenseReports] DROP COLUMN [ManagementFeedback]");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='ExpenseReports' AND COLUMN_NAME='ForwardedToAccountTeamOn') ALTER TABLE [ExpenseReports] DROP COLUMN [ForwardedToAccountTeamOn]");
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME='Notifications') DROP TABLE [Notifications]");
        }
    }
}
