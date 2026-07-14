using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SsoConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    OidcAuthority = table.Column<string>(type: "text", nullable: true),
                    OidcClientId = table.Column<string>(type: "text", nullable: true),
                    OidcClientSecret = table.Column<string>(type: "text", nullable: true),
                    SamlIdpEntityId = table.Column<string>(type: "text", nullable: true),
                    SamlIdpSsoUrl = table.Column<string>(type: "text", nullable: true),
                    SamlIdpCertificate = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsoConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SsoLoginStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Protocol = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SsoLoginStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SsoConfigs_TenantId",
                table: "SsoConfigs",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SsoLoginStates_TokenHash",
                table: "SsoLoginStates",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SsoConfigs");

            migrationBuilder.DropTable(
                name: "SsoLoginStates");
        }
    }
}
