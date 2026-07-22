using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConfluenceClone.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PageSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Pages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedById",
                table: "Pages",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "DeletedById",
                table: "Pages");
        }
    }
}
