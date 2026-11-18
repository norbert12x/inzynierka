using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inżynierka.Migrations
{
    /// <summary>
    /// Zadanie 3.2: macierz czasów przejazdów między atrakcjami (OpenRouteService) w schemacie routing.
    /// RLS bez polityk i brak uprawnień dla ról Supabase – backend łączy się bezpośrednio z PostgreSQL.
    /// </summary>
    public partial class TravelTimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "routing");

            migrationBuilder.CreateTable(
                name: "travel_times",
                schema: "routing",
                columns: table => new
                {
                    from_place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profile = table.Column<string>(type: "text", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    distance_meters = table.Column<int>(type: "integer", nullable: true),
                    provider = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_travel_times", x => new { x.from_place_id, x.to_place_id, x.profile });
                    table.CheckConstraint("travel_times_not_same_place_check", "from_place_id <> to_place_id");
                    table.CheckConstraint("travel_times_profile_check", "profile in ('driving-car', 'foot-walking')");
                    table.ForeignKey(
                        name: "FK_travel_times_places_from_place_id",
                        column: x => x.from_place_id,
                        principalSchema: "catalog",
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_travel_times_places_to_place_id",
                        column: x => x.to_place_id,
                        principalSchema: "catalog",
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_travel_times_to_place_id",
                schema: "routing",
                table: "travel_times",
                column: "to_place_id");

            migrationBuilder.Sql("""
                alter table routing.travel_times enable row level security;
                revoke all on schema routing from public, anon, authenticated;
                revoke all on all tables in schema routing from public, anon, authenticated;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "travel_times",
                schema: "routing");

            // EnsureSchema w Up nie ma odpowiednika w Down; restrict – nie usunie schematu, jeśli coś w nim zostało.
            migrationBuilder.Sql("drop schema if exists routing;");
        }
    }
}
