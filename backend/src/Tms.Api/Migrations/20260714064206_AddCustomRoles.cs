using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomRoleId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomRolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomRoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRolePermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRoles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomRolePermissions_CustomRoleId",
                table: "CustomRolePermissions",
                column: "CustomRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_TenantId_Name",
                table: "CustomRoles",
                columns: new[] { "TenantId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomRolePermissions");

            migrationBuilder.DropTable(
                name: "CustomRoles");

            migrationBuilder.DropColumn(
                name: "CustomRoleId",
                table: "Users");
        }
    }
}
