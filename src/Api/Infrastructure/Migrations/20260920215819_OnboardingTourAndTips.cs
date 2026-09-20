using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OnboardingTourAndTips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnboardingJson",
                table: "Users",
                type: "text",
                nullable: true);

            // True, not the generator's false: the property defaults to true,
            // and an account that existed before this shipped should get tips
            // like any other rather than silently having them off.
            migrationBuilder.AddColumn<bool>(
                name: "TipsEnabled",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // Everyone who already has an account has already used the
            // product, so the tour is marked skipped for them rather than
            // ambushing them with it on their next visit (dev-plan 10.3).
            // Accounts created from here on have a null column, which is
            // what makes the tour due.
            migrationBuilder.Sql(
                """
                UPDATE "Users"
                SET "OnboardingJson" = '{"tourSkippedAt":"1970-01-01T00:00:00+00:00","tourVersion":1,"tips":{}}'
                WHERE "OnboardingJson" IS NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TipsEnabled",
                table: "Users");
        }
    }
}
