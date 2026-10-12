using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseManagement.Migrations
{
    /// <inheritdoc />
    public partial class RedesignAttendanceModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AttendanceMode",
                table: "Attendances",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLate",
                table: "Attendances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LateMarkNumber",
                table: "Attendances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateReason",
                table: "Attendances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateReviewRemarks",
                table: "Attendances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LateReviewStatus",
                table: "Attendances",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LateReviewedBy",
                table: "Attendances",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LateReviewedOn",
                table: "Attendances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitLocation",
                table: "Attendances",
                type: "text",
                nullable: true);


            migrationBuilder.CreateIndex(
                name: "IX_Attendances_LateReviewedBy",
                table: "Attendances",
                column: "LateReviewedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Users_LateReviewedBy",
                table: "Attendances",
                column: "LateReviewedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Users_LateReviewedBy",
                table: "Attendances");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_LateReviewedBy",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "AttendanceMode",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "IsLate",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LateMarkNumber",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LateReason",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LateReviewRemarks",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LateReviewStatus",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LateReviewedBy",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "LateReviewedOn",
                table: "Attendances");

            migrationBuilder.DropColumn(
                name: "VisitLocation",
                table: "Attendances");

            
        }
    }
}
