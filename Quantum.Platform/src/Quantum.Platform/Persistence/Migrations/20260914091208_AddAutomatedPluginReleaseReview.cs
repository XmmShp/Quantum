using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quantum.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutomatedPluginReleaseReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutomatedReviewAttempts",
                table: "PluginRelease",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutomatedReviewCompletedAtUtc",
                table: "PluginRelease",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutomatedReviewStartedAtUtc",
                table: "PluginRelease",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "AutomatedReviewState",
                table: "PluginRelease",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "AutomatedReviewSummary",
                table: "PluginRelease",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutomatedReviewTaskId",
                table: "PluginRelease",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConcurrencyVersion",
                table: "PluginRelease",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_PluginRelease_AutomatedReviewState_UploadedAtUtc",
                table: "PluginRelease",
                columns: new[] { "AutomatedReviewState", "UploadedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PluginRelease_AutomatedReviewState_UploadedAtUtc",
                table: "PluginRelease");

            migrationBuilder.DropColumn(
                name: "AutomatedReviewAttempts",
                table: "PluginRelease");

            migrationBuilder.DropColumn(
                name: "AutomatedReviewCompletedAtUtc",
                table: "PluginRelease");

            migrationBuilder.DropColumn(
                name: "AutomatedReviewStartedAtUtc",
                table: "PluginRelease");

            migrationBuilder.DropColumn(
                name: "AutomatedReviewState",
                table: "PluginRelease");

            migrationBuilder.DropColumn(
                name: "AutomatedReviewSummary",
                table: "PluginRelease");

            migrationBuilder.DropColumn(
                name: "AutomatedReviewTaskId",
                table: "PluginRelease");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                table: "PluginRelease");
        }
    }
}
