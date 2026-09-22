using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RestoreFromAdminPage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeptCopyJson",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRestoreFrom",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastRestoreJobId",
                table: "SiteSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRestoredAt",
                table: "SiteSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RestoreCancelRequestedAt",
                table: "SiteSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RestoreJobId",
                table: "SiteSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RestoreStartedAt",
                table: "SiteSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OptionsJson",
                table: "BackupJobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeptCopyJson",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "LastRestoreFrom",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "LastRestoreJobId",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "LastRestoredAt",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "RestoreCancelRequestedAt",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "RestoreJobId",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "RestoreStartedAt",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "OptionsJson",
                table: "BackupJobs");
        }
    }
}
