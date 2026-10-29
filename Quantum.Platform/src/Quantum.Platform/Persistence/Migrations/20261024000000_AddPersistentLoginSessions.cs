using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Quantum.Platform.Persistence.Migrations;

public partial class AddPersistentLoginSessions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("PlatformLoginSession", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            UserId = table.Column<long>(type: "bigint", nullable: false),
            PasswordStamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            UsedTokensJson = table.Column<string>(type: "text", nullable: false),
            LastRequestId = table.Column<Guid>(type: "uuid", nullable: true),
            RotatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
            __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
        }, constraints: table => table.PrimaryKey("PK_PlatformLoginSession", row => row.Id));
        migrationBuilder.CreateIndex("IX_PlatformLoginSession_UserId", "PlatformLoginSession", "UserId");
        migrationBuilder.CreateIndex("IX_PlatformLoginSession_ExpiresAtUtc", "PlatformLoginSession", "ExpiresAtUtc");
        migrationBuilder.CreateIndex("IX_PlatformLoginSession___DeletedAtUnixTime", "PlatformLoginSession", "__DeletedAtUnixTime");
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("PlatformLoginSession");
}
