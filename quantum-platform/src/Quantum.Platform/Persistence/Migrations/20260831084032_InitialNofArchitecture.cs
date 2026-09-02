using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quantum.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialNofArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    Details = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ListingId = table.Column<long>(type: "bigint", nullable: true),
                    ReleaseId = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MarketUser",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Roles = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketUser", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NOFInboxMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Route = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    MessageType = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ClaimedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    OrderKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: true),
                    CompletesOrderKey = table.Column<bool>(type: "boolean", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOFInboxMessage", x => new { x.Id, x.Route });
                });

            migrationBuilder.CreateTable(
                name: "NOFInboxOrderState",
                columns: table => new
                {
                    Route = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    OrderKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    NextSequence = table.Column<long>(type: "bigint", nullable: false),
                    ClaimedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BlockedSequence = table.Column<long>(type: "bigint", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOFInboxOrderState", x => new { x.Route, x.OrderKey });
                });

            migrationBuilder.CreateTable(
                name: "NOFOutboxMessage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    MessageType = table.Column<int>(type: "integer", nullable: false),
                    DispatchRoutes = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<byte[]>(type: "bytea", nullable: false),
                    Headers = table.Column<string>(type: "text", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ClaimedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TraceParent = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OrderKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Sequence = table.Column<long>(type: "bigint", nullable: true),
                    CompletesOrderKey = table.Column<bool>(type: "boolean", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOFOutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NOFOutboxOrderState",
                columns: table => new
                {
                    OrderKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    CompletesOrderKey = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOFOutboxOrderState", x => new { x.OrderKey, x.Sequence });
                });

            migrationBuilder.CreateTable(
                name: "NOFTenant",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOFTenant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginListing",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    PluginId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AuthorUserId = table.Column<long>(type: "bigint", nullable: false),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginListing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginRelease",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ListingId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    QuantumVersionSupport = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReleaseNotes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    PackagePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    PackageSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PackageSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ReviewNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DownloadCount = table.Column<long>(type: "bigint", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginRelease", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry___DeletedAtUnixTime",
                table: "AuditEntry",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry_ActorUserId_OccurredAtUtc",
                table: "AuditEntry",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry_ListingId_OccurredAtUtc",
                table: "AuditEntry",
                columns: new[] { "ListingId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntry_OccurredAtUtc",
                table: "AuditEntry",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MarketUser___DeletedAtUnixTime",
                table: "MarketUser",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_MarketUser_Email",
                table: "MarketUser",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_MarketUser_Email___DeletedAtUnixTime",
                table: "MarketUser",
                columns: new[] { "Email", "__DeletedAtUnixTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketUser_Username",
                table: "MarketUser",
                column: "Username");

            migrationBuilder.CreateIndex(
                name: "IX_MarketUser_Username___DeletedAtUnixTime",
                table: "MarketUser",
                columns: new[] { "Username", "__DeletedAtUnixTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxMessage___DeletedAtUnixTime",
                table: "NOFInboxMessage",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxMessage_ClaimedBy",
                table: "NOFInboxMessage",
                column: "ClaimedBy");

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxMessage_Route_OrderKey_Sequence",
                table: "NOFInboxMessage",
                columns: new[] { "Route", "OrderKey", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxMessage_Status_ClaimExpiresAtUtc",
                table: "NOFInboxMessage",
                columns: new[] { "Status", "ClaimExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxMessage_Status_CreatedAtUtc",
                table: "NOFInboxMessage",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxOrderState___DeletedAtUnixTime",
                table: "NOFInboxOrderState",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxOrderState_ClaimedBy_ClaimExpiresAtUtc",
                table: "NOFInboxOrderState",
                columns: new[] { "ClaimedBy", "ClaimExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NOFInboxOrderState_UpdatedAtUtc",
                table: "NOFInboxOrderState",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxMessage___DeletedAtUnixTime",
                table: "NOFOutboxMessage",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxMessage_ClaimedBy",
                table: "NOFOutboxMessage",
                column: "ClaimedBy");

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxMessage_OrderKey_Sequence",
                table: "NOFOutboxMessage",
                columns: new[] { "OrderKey", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxMessage_Status_ClaimExpiresAtUtc",
                table: "NOFOutboxMessage",
                columns: new[] { "Status", "ClaimExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxMessage_Status_CreatedAtUtc",
                table: "NOFOutboxMessage",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxMessage_TraceParent",
                table: "NOFOutboxMessage",
                column: "TraceParent");

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxOrderState___DeletedAtUnixTime",
                table: "NOFOutboxOrderState",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_NOFOutboxOrderState_CreatedAtUtc",
                table: "NOFOutboxOrderState",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_NOFTenant___DeletedAtUnixTime",
                table: "NOFTenant",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_NOFTenant_Name",
                table: "NOFTenant",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_NOFTenant_Name___DeletedAtUnixTime",
                table: "NOFTenant",
                columns: new[] { "Name", "__DeletedAtUnixTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PluginListing___DeletedAtUnixTime",
                table: "PluginListing",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_PluginListing_AuthorUserId_UpdatedAtUtc",
                table: "PluginListing",
                columns: new[] { "AuthorUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PluginListing_PluginId",
                table: "PluginListing",
                column: "PluginId");

            migrationBuilder.CreateIndex(
                name: "IX_PluginListing_PluginId___DeletedAtUnixTime",
                table: "PluginListing",
                columns: new[] { "PluginId", "__DeletedAtUnixTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PluginRelease___DeletedAtUnixTime",
                table: "PluginRelease",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_PluginRelease_ListingId_Version",
                table: "PluginRelease",
                columns: new[] { "ListingId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_PluginRelease_ListingId_Version___DeletedAtUnixTime",
                table: "PluginRelease",
                columns: new[] { "ListingId", "Version", "__DeletedAtUnixTime" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PluginRelease_Status_UploadedAtUtc",
                table: "PluginRelease",
                columns: new[] { "Status", "UploadedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntry");

            migrationBuilder.DropTable(
                name: "MarketUser");

            migrationBuilder.DropTable(
                name: "NOFInboxMessage");

            migrationBuilder.DropTable(
                name: "NOFInboxOrderState");

            migrationBuilder.DropTable(
                name: "NOFOutboxMessage");

            migrationBuilder.DropTable(
                name: "NOFOutboxOrderState");

            migrationBuilder.DropTable(
                name: "NOFTenant");

            migrationBuilder.DropTable(
                name: "PluginListing");

            migrationBuilder.DropTable(
                name: "PluginRelease");
        }
    }
}
