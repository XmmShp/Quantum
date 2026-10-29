using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quantum.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistrationEmailVerification",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationEmailVerification", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailVerification___DeletedAtUnixTime",
                table: "RegistrationEmailVerification",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailVerification_Email",
                table: "RegistrationEmailVerification",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailVerification_Email___DeletedAtUnixTime",
                table: "RegistrationEmailVerification",
                columns: new[] { "Email", "__DeletedAtUnixTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationEmailVerification_ExpiresAtUtc",
                table: "RegistrationEmailVerification",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationEmailVerification");
        }
    }
}
