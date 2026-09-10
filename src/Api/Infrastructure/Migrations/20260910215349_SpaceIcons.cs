using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceIcons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "IconColor",
                table: "Spaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IconKind",
                table: "Spaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IconValue",
                table: "Spaces",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IconColor",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "IconKind",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "IconValue",
                table: "Spaces");
        }
    }
}
