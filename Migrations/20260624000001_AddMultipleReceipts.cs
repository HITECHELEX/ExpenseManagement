using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    /// <inheritdoc />
    [Migration("20260624000001_AddMultipleReceipts")]
    public partial class AddMultipleReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ExpenseClaims""
                    ADD COLUMN IF NOT EXISTS ""Receipt2Path""     text,
                    ADD COLUMN IF NOT EXISTS ""Receipt2FileName"" text,
                    ADD COLUMN IF NOT EXISTS ""Receipt3Path""     text,
                    ADD COLUMN IF NOT EXISTS ""Receipt3FileName"" text;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""ExpenseClaims""
                    DROP COLUMN IF EXISTS ""Receipt2Path"",
                    DROP COLUMN IF EXISTS ""Receipt2FileName"",
                    DROP COLUMN IF EXISTS ""Receipt3Path"",
                    DROP COLUMN IF EXISTS ""Receipt3FileName"";
            ");
        }
    }
}
