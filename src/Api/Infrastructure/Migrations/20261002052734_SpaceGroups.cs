using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <summary>
    /// Groups for every space (dev-plan 21.1): the schema only. The data (each
    /// space's four groups, its EveryoneAccess, and person grants moved into
    /// the groups) is done at start by <c>SpaceGroupSeed</c>, in C#, because
    /// tests build the schema without migrations. Down drops the columns but
    /// cannot put the moved grants back: rolling back is a restore, and
    /// <c>SpaceGrantMoves</c> is the record for reversing a move by hand.
    /// </summary>
    public partial class SpaceGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Groups_NormalizedName",
                table: "Groups");

            migrationBuilder.AddColumn<int>(
                name: "EveryoneAccess",
                table: "Spaces",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpaceId",
                table: "Groups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpaceRole",
                table: "Groups",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpaceGrantMoves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    GrantCreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpaceGrantMoves", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_NormalizedName",
                table: "Groups",
                column: "NormalizedName",
                unique: true,
                filter: "\"SpaceId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_SpaceId_SpaceRole",
                table: "Groups",
                columns: new[] { "SpaceId", "SpaceRole" },
                unique: true,
                filter: "\"SpaceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SpaceGrantMoves_SpaceId",
                table: "SpaceGrantMoves",
                column: "SpaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Spaces_SpaceId",
                table: "Groups",
                column: "SpaceId",
                principalTable: "Spaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Groups_Spaces_SpaceId",
                table: "Groups");

            migrationBuilder.DropTable(
                name: "SpaceGrantMoves");

            migrationBuilder.DropIndex(
                name: "IX_Groups_NormalizedName",
                table: "Groups");

            migrationBuilder.DropIndex(
                name: "IX_Groups_SpaceId_SpaceRole",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "EveryoneAccess",
                table: "Spaces");

            migrationBuilder.DropColumn(
                name: "SpaceId",
                table: "Groups");

            migrationBuilder.DropColumn(
                name: "SpaceRole",
                table: "Groups");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_NormalizedName",
                table: "Groups",
                column: "NormalizedName",
                unique: true);
        }
    }
}
