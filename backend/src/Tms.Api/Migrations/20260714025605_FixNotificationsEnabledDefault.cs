using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Api.Migrations
{
    /// <inheritdoc />
    public partial class FixNotificationsEnabledDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "NotificationsEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<bool>(
                name: "NotificationsEnabled",
                table: "PortalCustomers",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            // Data fix: the AddNotifications migration added this column with
            // DEFAULT FALSE (a bug - the C# `= true` property initializer
            // never reached the SQL default), so every row that existed before
            // that migration ran got silently opted out. Nobody could have
            // deliberately muted notifications yet - there was no UI for it
            // until this same deploy - so it's safe to flip every existing
            // false row back to the intended true default in one pass.
            migrationBuilder.Sql("UPDATE \"Users\" SET \"NotificationsEnabled\" = true WHERE \"NotificationsEnabled\" = false;");
            migrationBuilder.Sql("UPDATE \"PortalCustomers\" SET \"NotificationsEnabled\" = true WHERE \"NotificationsEnabled\" = false;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "NotificationsEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<bool>(
                name: "NotificationsEnabled",
                table: "PortalCustomers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }
    }
}
