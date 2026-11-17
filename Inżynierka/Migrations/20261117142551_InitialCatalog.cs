using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inżynierka.Migrations
{
    /// <summary>
    /// Migracja startowa (baseline). Tabele katalogu już istnieją – utworzyły je pliki
    /// database/001–004 – więc nie tworzymy ich ponownie. Snapshot (Designer.cs i
    /// CatalogDbContextModelSnapshot.cs) odwzorowuje ten stan dla kolejnych migracji.
    /// Up wykonuje tylko database/005 (czasy wizyt kategorii), bezpieczny do ponowienia.
    /// </summary>
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Treść database/005_category_visit_minutes.sql (bez begin/commit – migracja ma własną transakcję).
            migrationBuilder.Sql("""
                alter table catalog.categories add column if not exists default_visit_minutes integer;

                update catalog.categories c
                set default_visit_minutes = v.minutes
                from (values
                    ('museums', 90),
                    ('heritage', 30),
                    ('religious-sites', 20),
                    ('nature', 60),
                    ('viewpoints', 20),
                    ('recreation', 120),
                    ('culture', 60),
                    ('public-art', 10),
                    ('other', 30)
                ) as v(code, minutes)
                where c.code = v.code and c.default_visit_minutes is null;

                update catalog.categories set default_visit_minutes = 30 where default_visit_minutes is null;

                alter table catalog.categories alter column default_visit_minutes set not null;
                alter table catalog.categories drop constraint if exists categories_default_visit_minutes_check;
                alter table catalog.categories add constraint categories_default_visit_minutes_check
                    check (default_visit_minutes between 5 and 600);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Celowo puste: cofnięcie migracji startowej nie usuwa katalogu ani danych.
        }
    }
}
