using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectorNameColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add DirectorName column to ExpenseReports (IF NOT EXISTS is safe to re-run)
            migrationBuilder.Sql(@"
                ALTER TABLE ""ExpenseReports""
                    ADD COLUMN IF NOT EXISTS ""DirectorName"" text;
            ");

            // Also ensure AccountTeamProfileName exists (safety net in case previous migration was partial)
            migrationBuilder.Sql(@"
                ALTER TABLE ""ExpenseReports""
                    ADD COLUMN IF NOT EXISTS ""AccountTeamProfileName"" text;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"ALTER TABLE ""ExpenseReports"" DROP COLUMN IF EXISTS ""DirectorName"";");
        }
    }
}
