using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmbedsAndLinkPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The default has to be the column default, not just the C#
            // property initialiser: that initialiser only runs for a *new*
            // SiteSettings object, so on an instance that already has its
            // settings row an empty default would silently turn embeds off
            // on upgrade. Found by upgrading a running instance: a fresh
            // test database never takes this path.
            migrationBuilder.AddColumn<string>(
                name: "EmbedAllowlist",
                table: "SiteSettings",
                type: "text",
                nullable: false,
                defaultValue: string.Join('\n', Tesria.Api.Domain.SiteSettings.DefaultEmbedAllowlist));

            migrationBuilder.CreateTable(
                name: "LinkPreviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UrlHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SiteName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkPreviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LinkPreviews_UrlHash",
                table: "LinkPreviews",
                column: "UrlHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LinkPreviews");

            migrationBuilder.DropColumn(
                name: "EmbedAllowlist",
                table: "SiteSettings");
        }
    }
}
