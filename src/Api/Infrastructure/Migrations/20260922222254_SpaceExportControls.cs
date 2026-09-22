using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SpaceExportControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExportHtml",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExportMarkdown",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExportPack",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExportPdf",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExportSite",
                table: "Spaces",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExportHtml",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "ExportMarkdown",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "ExportPack",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "ExportPdf",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "ExportSite",
                table: "Spaces");
        }
    }
}
