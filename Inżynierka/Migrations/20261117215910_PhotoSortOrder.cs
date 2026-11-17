using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inżynierka.Migrations
{
    /// <inheritdoc />
    public partial class PhotoSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                schema: "catalog",
                table: "place_photos",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort_order",
                schema: "catalog",
                table: "place_photos");
        }
    }
}
