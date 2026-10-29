using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quantum.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameMarketUserToPlatformUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MarketUser",
                table: "MarketUser");

            migrationBuilder.RenameTable(
                name: "MarketUser",
                newName: "PlatformUser");

            migrationBuilder.RenameIndex(
                name: "IX_MarketUser___DeletedAtUnixTime",
                table: "PlatformUser",
                newName: "IX_PlatformUser___DeletedAtUnixTime");

            migrationBuilder.RenameIndex(
                name: "IX_MarketUser_Email",
                table: "PlatformUser",
                newName: "IX_PlatformUser_Email");

            migrationBuilder.RenameIndex(
                name: "IX_MarketUser_Email___DeletedAtUnixTime",
                table: "PlatformUser",
                newName: "IX_PlatformUser_Email___DeletedAtUnixTime");

            migrationBuilder.RenameIndex(
                name: "IX_MarketUser_Username",
                table: "PlatformUser",
                newName: "IX_PlatformUser_Username");

            migrationBuilder.RenameIndex(
                name: "IX_MarketUser_Username___DeletedAtUnixTime",
                table: "PlatformUser",
                newName: "IX_PlatformUser_Username___DeletedAtUnixTime");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlatformUser",
                table: "PlatformUser",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PlatformUser",
                table: "PlatformUser");

            migrationBuilder.RenameTable(
                name: "PlatformUser",
                newName: "MarketUser");

            migrationBuilder.RenameIndex(
                name: "IX_PlatformUser___DeletedAtUnixTime",
                table: "MarketUser",
                newName: "IX_MarketUser___DeletedAtUnixTime");

            migrationBuilder.RenameIndex(
                name: "IX_PlatformUser_Email",
                table: "MarketUser",
                newName: "IX_MarketUser_Email");

            migrationBuilder.RenameIndex(
                name: "IX_PlatformUser_Email___DeletedAtUnixTime",
                table: "MarketUser",
                newName: "IX_MarketUser_Email___DeletedAtUnixTime");

            migrationBuilder.RenameIndex(
                name: "IX_PlatformUser_Username",
                table: "MarketUser",
                newName: "IX_MarketUser_Username");

            migrationBuilder.RenameIndex(
                name: "IX_PlatformUser_Username___DeletedAtUnixTime",
                table: "MarketUser",
                newName: "IX_MarketUser_Username___DeletedAtUnixTime");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MarketUser",
                table: "MarketUser",
                column: "Id");
        }
    }
}
