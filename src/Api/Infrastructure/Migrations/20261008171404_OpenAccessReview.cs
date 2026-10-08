using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <summary>
    /// The open-space review (dev-plan 21.5): when someone chose to let
    /// everyone signed in administer a space. Null on every existing space,
    /// so each one open that way since before 21.1 is listed for review;
    /// nothing about anyone's access changes here.
    /// </summary>
    public partial class OpenAccessReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EveryoneAdminConfirmedAt",
                table: "Spaces",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EveryoneAdminConfirmedAt",
                table: "Spaces");
        }
    }
}
