using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PageRestrictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalType = table.Column<int>(type: "integer", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageRestrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PageRestrictions_Pages_PageId",
                        column: x => x.PageId,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpacePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SpaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalType = table.Column<int>(type: "integer", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpacePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpacePermissions_Spaces_SpaceId",
                        column: x => x.SpaceId,
                        principalTable: "Spaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PageRestrictions_PageId_PrincipalType_PrincipalId_Operation",
                table: "PageRestrictions",
                columns: new[] { "PageId", "PrincipalType", "PrincipalId", "Operation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SpacePermissions_SpaceId_PrincipalType_PrincipalId_Operation",
                table: "SpacePermissions",
                columns: new[] { "SpaceId", "PrincipalType", "PrincipalId", "Operation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PageRestrictions");

            migrationBuilder.DropTable(
                name: "SpacePermissions");
        }
    }
}
