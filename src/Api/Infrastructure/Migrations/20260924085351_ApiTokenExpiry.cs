using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ApiTokenExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiryWarnedAt",
                table: "ApiTokens",
                type: "timestamp with time zone",
                nullable: true);

            // Tokens that existed before expiry get 90 days from the upgrade,
            // the new default, rather than "never": nothing stops working at
            // once, and their owners are warned a week ahead (dev-plan 14.1).
            migrationBuilder.Sql("UPDATE \"ApiTokens\" SET \"ExpiresAt\" = now() + interval '90 days' WHERE \"ExpiresAt\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ApiTokens");

            migrationBuilder.DropColumn(
                name: "ExpiryWarnedAt",
                table: "ApiTokens");
        }
    }
}
