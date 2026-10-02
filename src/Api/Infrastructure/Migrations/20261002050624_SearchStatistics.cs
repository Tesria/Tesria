using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Tesria.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SearchStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SearchTextVersion",
                table: "SiteSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Pages",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('english', normalize(regexp_replace(normalize(translate(\"Title\", '/', ' '), NFD), '[\\u0300-\\u036f]', '', 'g'), NFC)), 'A') || to_tsvector('english', normalize(regexp_replace(normalize(translate(\"SearchText\", '/', ' '), NFD), '[\\u0300-\\u036f]', '', 'g'), NFC))",
                stored: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true)
                .OldAnnotation("Npgsql:TsVectorConfig", "english")
                .OldAnnotation("Npgsql:TsVectorProperties", new[] { "SearchText" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchTextVersion",
                table: "SiteSettings");

            migrationBuilder.AlterColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Pages",
                type: "tsvector",
                nullable: true,
                oldClrType: typeof(NpgsqlTsVector),
                oldType: "tsvector",
                oldNullable: true,
                oldComputedColumnSql: "setweight(to_tsvector('english', normalize(regexp_replace(normalize(translate(\"Title\", '/', ' '), NFD), '[\\u0300-\\u036f]', '', 'g'), NFC)), 'A') || to_tsvector('english', normalize(regexp_replace(normalize(translate(\"SearchText\", '/', ' '), NFD), '[\\u0300-\\u036f]', '', 'g'), NFC))")
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "SearchText" });
        }
    }
}
