using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tms.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Escalated",
                table: "Tickets",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstRespondedAt",
                table: "Tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResponseDueAt",
                table: "Tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "SlaPolicies",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Escalated",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "FirstRespondedAt",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ResponseDueAt",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "SlaPolicies");
        }
    }
}
