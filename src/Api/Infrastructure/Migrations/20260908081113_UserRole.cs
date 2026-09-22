using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill: promote the earliest-created account on instances that
            // already have users, so an existing install is never left with
            // content and nobody able to administer it. New instances have no
            // rows here and this is a no-op; the first registration then takes
            // the role instead (AuthEndpoints.Register).
            //
            // ORDER BY "CreatedAt", "Id": the id breaks ties so the result is
            // deterministic if two accounts share a timestamp.
            migrationBuilder.Sql("""
                UPDATE "Users" SET "Role" = 1
                WHERE "Id" = (
                    SELECT "Id" FROM "Users" ORDER BY "CreatedAt", "Id" LIMIT 1
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");
        }
    }
}
