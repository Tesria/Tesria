using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InstanceBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AccentName",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccentPolicy",
                table: "SiteSettings",
                type: "text",
                nullable: false,
                defaultValue: "any");

            migrationBuilder.AddColumn<string>(
                name: "BrandAccentDark",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandAccentLight",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BrandChangedAt",
                table: "SiteSettings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BrandChangedById",
                table: "SiteSettings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandDisplay",
                table: "SiteSettings",
                type: "text",
                nullable: false,
                defaultValue: "logo-and-name");

            migrationBuilder.AddColumn<bool>(
                name: "BrandFaviconHasSvg",
                table: "SiteSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BrandFaviconHash",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandLogoDarkFormat",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandLogoDarkHash",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandLogoDarkHeight",
                table: "SiteSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandLogoDarkWidth",
                table: "SiteSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandLogoFormat",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandLogoHash",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandLogoHeight",
                table: "SiteSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BrandLogoWidth",
                table: "SiteSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrandName",
                table: "SiteSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignInArrangement",
                table: "SiteSettings",
                type: "text",
                nullable: false,
                defaultValue: "side-by-side");

            migrationBuilder.AddColumn<string>(
                name: "ThemePolicy",
                table: "SiteSettings",
                type: "text",
                nullable: false,
                defaultValue: "any");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccentName",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "AccentPolicy",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandAccentDark",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandAccentLight",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandChangedAt",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandChangedById",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandDisplay",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandFaviconHasSvg",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandFaviconHash",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoDarkFormat",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoDarkHash",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoDarkHeight",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoDarkWidth",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoFormat",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoHash",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoHeight",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandLogoWidth",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "BrandName",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "SignInArrangement",
                table: "SiteSettings");

            migrationBuilder.DropColumn(
                name: "ThemePolicy",
                table: "SiteSettings");
        }
    }
}
