using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MailSignIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleClientId",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleClientSecretProtected",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailOAuthAccount",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MailOAuthConnectedAt",
                table: "SiteSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailOAuthError",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailOAuthRefreshTokenProtected",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MicrosoftClientId",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MicrosoftClientSecretProtected",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MicrosoftTenant",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpProvider",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpSignIn",
                table: "SiteSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleClientId",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "GoogleClientSecretProtected",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "MailOAuthAccount",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "MailOAuthConnectedAt",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "MailOAuthError",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "MailOAuthRefreshTokenProtected",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "MicrosoftClientId",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "MicrosoftClientSecretProtected",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "MicrosoftTenant",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "SmtpProvider",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "SmtpSignIn",
                table: "SiteSettings");
        }
    }
}
