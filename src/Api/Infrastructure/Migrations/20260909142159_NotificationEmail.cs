using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NotificationEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmailNotifications",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastDigestAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailedAt",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailNotifications",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastDigestAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailedAt",
                table: "Notifications");
        }
    }
}
