using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackupTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupTargets",
                columns: table => new
                {
                    Slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Bucket = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Prefix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Problem = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    KeyFingerprint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    PassphraseFingerprint = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LastBackupAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastWalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastVerifyAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BytesStored = table.Column<long>(type: "bigint", nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupTargets", x => x.Slot);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupTargets");
        }
    }
}
