using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quantum.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordResetVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PasswordResetVerification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetVerification", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetVerification___DeletedAtUnixTime",
                table: "PasswordResetVerification",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetVerification_Email",
                table: "PasswordResetVerification",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetVerification_Email___DeletedAtUnixTime",
                table: "PasswordResetVerification",
                columns: new[] { "Email", "__DeletedAtUnixTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetVerification_ExpiresAtUtc",
                table: "PasswordResetVerification",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordResetVerification");
        }
    }
}
