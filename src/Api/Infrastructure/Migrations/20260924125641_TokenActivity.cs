using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TokenActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastUsedFrom",
                table: "ApiTokens",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "UseCount",
                table: "ApiTokens",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ApiTokenDays",
                columns: table => new
                {
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Reads = table.Column<int>(type: "integer", nullable: false),
                    Writes = table.Column<int>(type: "integer", nullable: false),
                    McpReads = table.Column<int>(type: "integer", nullable: false),
                    McpWrites = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiTokenDays", x => new { x.TokenId, x.Day });
                    table.ForeignKey(
                        name: "FK_ApiTokenDays_ApiTokens_TokenId",
                        column: x => x.TokenId,
                        principalTable: "ApiTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "McpToolCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TokenPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tool = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Write = table.Column<bool>(type: "boolean", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpaceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Ok = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_McpToolCalls", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiTokenDays_Day",
                table: "ApiTokenDays",
                column: "Day");

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_At",
                table: "McpToolCalls",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_McpToolCalls_TokenId_At",
                table: "McpToolCalls",
                columns: new[] { "TokenId", "At" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiTokenDays");

            migrationBuilder.DropTable(
                name: "McpToolCalls");

            migrationBuilder.DropColumn(
                name: "LastUsedFrom",
                table: "ApiTokens");

            migrationBuilder.DropColumn(
                name: "UseCount",
                table: "ApiTokens");
        }
    }
}
