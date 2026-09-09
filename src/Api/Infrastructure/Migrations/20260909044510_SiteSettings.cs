using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SiteSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SiteSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AllowPublicRegistration = table.Column<bool>(type: "boolean", nullable: false),
                    AllowPublicSpaces = table.Column<bool>(type: "boolean", nullable: false),
                    EmailEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SmtpHost = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SmtpPort = table.Column<int>(type: "integer", nullable: false),
                    SmtpUsername = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    SmtpPasswordProtected = table.Column<string>(type: "text", nullable: true),
                    SmtpFromAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    SmtpTls = table.Column<int>(type: "integer", nullable: false),
                    RequireTotpForAdmins = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SiteSettings");
        }
    }
}
