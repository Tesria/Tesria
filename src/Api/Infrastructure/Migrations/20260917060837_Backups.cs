using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Backups : Migration
    {
        // The defaults match SiteSettings' own. They are not a policy: until
        // BackupPolicyChangedAt is set (BackupPolicySeed, at startup) the
        // sidecars remove nothing.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BackupKeepCount",
                table: "SiteSettings",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "BackupKeepDays",
                table: "SiteSettings",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BackupPolicyChangedAt",
                table: "SiteSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BackupPolicyChangedById",
                table: "SiteSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BackupRetentionEnabled",
                table: "SiteSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "BackupAgents",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IntervalHours = table.Column<int>(type: "integer", nullable: false),
                    FullEveryDays = table.Column<int>(type: "integer", nullable: true),
                    ToolVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VolumeFreeBytes = table.Column<long>(type: "bigint", nullable: true),
                    VolumeTotalBytes = table.Column<long>(type: "bigint", nullable: true),
                    WalArchivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedRetentionEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    AppliedKeepCount = table.Column<int>(type: "integer", nullable: true),
                    AppliedKeepDays = table.Column<int>(type: "integer", nullable: true),
                    PolicyObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupAgents", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "BackupJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Agent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Target = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RequestedById = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResultJson = table.Column<string>(type: "jsonb", nullable: true),
                    LogTail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Backups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Agent = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Prior = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DetailJson = table.Column<string>(type: "jsonb", nullable: true),
                    HasUploads = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedReason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LastVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerifyOk = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Backups", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_Agent_Status_RequestedAt",
                table: "BackupJobs",
                columns: new[] { "Agent", "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupJobs_RequestedAt",
                table: "BackupJobs",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Backups_Agent_Label",
                table: "Backups",
                columns: new[] { "Agent", "Label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupAgents");

            migrationBuilder.DropTable(
                name: "BackupJobs");

            migrationBuilder.DropTable(
                name: "Backups");

            migrationBuilder.DropColumn(
                name: "BackupKeepCount",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BackupKeepDays",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BackupPolicyChangedAt",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BackupPolicyChangedById",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BackupRetentionEnabled",
                table: "SiteSettings");
        }
    }
}
