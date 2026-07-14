using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetsAndCmdb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CmdbEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SerialNumberOrLicenseKey = table.Column<string>(type: "text", nullable: true),
                    AssignedToUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    PurchaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WarrantyExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImpersonationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformUserEmail = table.Column<string>(type: "text", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantName = table.Column<string>(type: "text", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserEmail = table.Column<string>(type: "text", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpersonationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TicketAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketAssets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_Name",
                table: "Assets",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationLogs_StartedAt",
                table: "ImpersonationLogs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TicketAssets_AssetId_TicketId",
                table: "TicketAssets",
                columns: new[] { "AssetId", "TicketId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketAssets_TicketId",
                table: "TicketAssets",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assets");

            migrationBuilder.DropTable(
                name: "ImpersonationLogs");

            migrationBuilder.DropTable(
                name: "TicketAssets");

            migrationBuilder.DropColumn(
                name: "CmdbEnabled",
                table: "Tenants");
        }
    }
}
