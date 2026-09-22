using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackupTargetKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_BackupTargets",
                table: "BackupTargets");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "BackupTargets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every row that exists was written by the pgBackRest
                // sidecar, which is the database repository; an empty
                // default would strand each one under a key nothing writes.
                defaultValue: "database");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BackupTargets",
                table: "BackupTargets",
                columns: new[] { "Slot", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_BackupTargets",
                table: "BackupTargets");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "BackupTargets");

            migrationBuilder.AddPrimaryKey(
                name: "PK_BackupTargets",
                table: "BackupTargets",
                column: "Slot");
        }
    }
}
