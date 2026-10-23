using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Quantum.Platform.Persistence.Migrations;

public partial class AddMobileBridge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("MobileBridgeAccount", columns: table => new
        {
            Id = table.Column<long>(type: "bigint", nullable: false),
            StateJson = table.Column<string>(type: "text", nullable: false),
            Version = table.Column<long>(type: "bigint", nullable: false),
            __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
        }, constraints: table => table.PrimaryKey("PK_MobileBridgeAccount", row => row.Id));
        migrationBuilder.CreateIndex("IX_MobileBridgeAccount___DeletedAtUnixTime", "MobileBridgeAccount", "__DeletedAtUnixTime");
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("MobileBridgeAccount");
}
