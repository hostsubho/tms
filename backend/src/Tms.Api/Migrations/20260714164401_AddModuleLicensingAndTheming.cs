using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddModuleLicensingAndTheming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ModuleBillingTotalOverrideCents",
                table: "Tenants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryColor",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccentColor",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThemeMode",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "Light");

            migrationBuilder.AddColumn<string>(
                name: "BorderRadius",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "Medium");

            migrationBuilder.AddColumn<string>(
                name: "Density",
                table: "Tenants",
                type: "text",
                nullable: false,
                defaultValue: "Comfortable");

            migrationBuilder.AddColumn<string>(
                name: "CustomCss",
                table: "Tenants",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TenantModuleFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleKey = table.Column<string>(type: "text", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyCostCents = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantModuleFlags", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantModuleFlags_TenantId_ModuleKey",
                table: "TenantModuleFlags",
                columns: new[] { "TenantId", "ModuleKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantModuleFlags");

            migrationBuilder.DropColumn(
                name: "ModuleBillingTotalOverrideCents",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SecondaryColor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "AccentColor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ThemeMode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BorderRadius",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "Density",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CustomCss",
                table: "Tenants");
        }
    }
}
