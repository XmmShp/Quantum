using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Quantum.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOidcCredentialClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OAuthClient",
                columns: table => new
                {
                    ClientId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SecretHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SecretSalt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JsonWebKeySet = table.Column<string>(type: "text", nullable: false),
                    AllowedScopes = table.Column<string>(type: "text", nullable: false),
                    RedirectUris = table.Column<string>(type: "text", nullable: false),
                    AccessTokenClaims = table.Column<string>(type: "text", nullable: false),
                    TokenEndpointAuthenticationMethod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AllowedGrantTypes = table.Column<string>(type: "text", nullable: false),
                    AllowedResponseTypes = table.Column<string>(type: "text", nullable: false),
                    ApplicationType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RegistrationMetadata = table.Column<string>(type: "text", nullable: false),
                    RegistrationAccessTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RegistrationAccessTokenSalt = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ClientType = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OAuthClient", x => x.ClientId);
                });

            migrationBuilder.CreateTable(
                name: "PersistedSigningKey",
                columns: table => new
                {
                    Kid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EncryptedPrivateKey = table.Column<string>(type: "text", nullable: false),
                    PublicKey = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InvalidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersistedSigningKey", x => x.Kid);
                });

            migrationBuilder.CreateTable(
                name: "RevokedRefreshToken",
                columns: table => new
                {
                    TokenId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    __DeletedAtUnixTime = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevokedRefreshToken", x => x.TokenId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OAuthClient___DeletedAtUnixTime",
                table: "OAuthClient",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_OAuthClient_IsEnabled",
                table: "OAuthClient",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedSigningKey___DeletedAtUnixTime",
                table: "PersistedSigningKey",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_PersistedSigningKey_Status_CreatedAtUtc",
                table: "PersistedSigningKey",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PersistedSigningKey_Status_InvalidatedAtUtc",
                table: "PersistedSigningKey",
                columns: new[] { "Status", "InvalidatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RevokedRefreshToken___DeletedAtUnixTime",
                table: "RevokedRefreshToken",
                column: "__DeletedAtUnixTime");

            migrationBuilder.CreateIndex(
                name: "IX_RevokedRefreshToken_ExpiresAtUtc",
                table: "RevokedRefreshToken",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OAuthClient");

            migrationBuilder.DropTable(
                name: "PersistedSigningKey");

            migrationBuilder.DropTable(
                name: "RevokedRefreshToken");
        }
    }
}
