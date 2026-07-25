using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConfluenceClone.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OidcSubjectIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_OidcSubject",
                table: "Users",
                column: "OidcSubject",
                unique: true,
                filter: "\"OidcSubject\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_OidcSubject",
                table: "Users");
        }
    }
}
