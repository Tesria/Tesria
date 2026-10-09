using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <summary>
    /// Default access for new spaces (dev-plan 21.6). The settings row an
    /// upgraded instance already has gets the defaults the owner chose for a
    /// new one: Tesria's administrators administer, everyone signed in edits.
    /// Only spaces made afterwards start from them; no existing space changes.
    /// </summary>
    public partial class NewSpaceDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NewSpaceEveryoneAccess",
                table: "SiteSettings",
                type: "integer",
                nullable: true,
                // The existing row (an upgraded instance) gets what a new one
                // starts with: everyone signed in edits.
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "NewSpaceGroupsJson",
                table: "SiteSettings",
                type: "text",
                nullable: false,
                // ...and Tesria's administrators administer.
                defaultValue: Tesria.Api.Domain.SiteSettings.DefaultNewSpaceGroupsJson);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NewSpaceEveryoneAccess",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "NewSpaceGroupsJson",
                table: "SiteSettings");
        }
    }
}
