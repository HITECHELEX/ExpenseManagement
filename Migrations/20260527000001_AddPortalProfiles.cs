using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. PortalProfiles table ─────────────────────────────────────────────
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""PortalProfiles"" (
                    ""Id""        serial        PRIMARY KEY,
                    ""Name""      varchar(150)  NOT NULL DEFAULT '',
                    ""Role""      int           NOT NULL DEFAULT 0,
                    ""CompanyId"" int           NULL REFERENCES ""Companies""(""Id"") ON DELETE CASCADE,
                    ""IsActive""  boolean       NOT NULL DEFAULT TRUE,
                    ""CreatedAt"" timestamptz   NOT NULL DEFAULT NOW()
                );
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_PortalProfiles_Role_CompanyId_IsActive""
                    ON ""PortalProfiles"" (""Role"", ""CompanyId"", ""IsActive"");
            ");

            // ── 2. AccountTeamProfileName column on ExpenseReports ─────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE ""ExpenseReports""
                    ADD COLUMN IF NOT EXISTS ""AccountTeamProfileName"" text;
            ");

            // ── 3. Seed Account Team profiles ─────────────────────────────────────
            // HITECHELEXLLP (CompanyId=1): Ankit, Avinash
            // CEYONE SOLUTIONS PVT LTD (CompanyId=2): Kailash
            // Management directors: CompanyId NULL
            migrationBuilder.Sql(@"
                INSERT INTO ""PortalProfiles"" (""Id"", ""Name"", ""Role"", ""CompanyId"", ""IsActive"", ""CreatedAt"")
                VALUES
                    (1, 'Ankit',           1, 1,    TRUE, '2024-01-01T00:00:00Z'),
                    (2, 'Avinash',         1, 1,    TRUE, '2024-01-01T00:00:00Z'),
                    (3, 'Kailash',         1, 2,    TRUE, '2024-01-01T00:00:00Z'),
                    (4, 'Suvarna Bhutkar', 0, NULL, TRUE, '2024-01-01T00:00:00Z'),
                    (5, 'Ajay Bhutkar',    0, NULL, TRUE, '2024-01-01T00:00:00Z'),
                    (6, 'Vilas Bhosale',   0, NULL, TRUE, '2024-01-01T00:00:00Z')
                ON CONFLICT (""Id"") DO NOTHING;
            ");

            // Reset sequence so new inserts get Id >= 7
            migrationBuilder.Sql(@"SELECT setval('""PortalProfiles_Id_seq""', 7, FALSE);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""PortalProfiles"";");
            migrationBuilder.Sql(@"ALTER TABLE ""ExpenseReports"" DROP COLUMN IF EXISTS ""AccountTeamProfileName"";");
        }
    }
}
