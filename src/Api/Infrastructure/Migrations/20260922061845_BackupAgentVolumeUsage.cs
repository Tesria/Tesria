using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackupAgentVolumeUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "VolumeBackupBytes",
                table: "BackupAgents",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VolumeFilesystem",
                table: "BackupAgents",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VolumeBackupBytes",
                table: "BackupAgents");

            migrationBuilder.DropColumn(
                name: "VolumeFilesystem",
                table: "BackupAgents");
        }
    }
}
