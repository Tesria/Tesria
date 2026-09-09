using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PublicSpaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PublicComments",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublicSince",
                table: "Spaces",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "PublicComments",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "PublicSince",
                table: "Spaces");
        }
    }
}
