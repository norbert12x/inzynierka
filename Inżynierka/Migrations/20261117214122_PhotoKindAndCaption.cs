using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inżynierka.Migrations
{
    /// <inheritdoc />
    public partial class PhotoKindAndCaption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "caption",
                schema: "catalog",
                table: "place_photos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "catalog",
                table: "place_photos",
                type: "text",
                nullable: false,
                defaultValue: "place");

            migrationBuilder.AddCheckConstraint(
                name: "place_photos_kind_check",
                schema: "catalog",
                table: "place_photos",
                sql: "kind in ('place', 'exhibit')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "place_photos_kind_check",
                schema: "catalog",
                table: "place_photos");

            migrationBuilder.DropColumn(
                name: "caption",
                schema: "catalog",
                table: "place_photos");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "catalog",
                table: "place_photos");
        }
    }
}
