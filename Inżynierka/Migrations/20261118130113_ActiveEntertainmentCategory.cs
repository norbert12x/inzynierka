using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inżynierka.Migrations
{
    /// <summary>
    /// Zadanie 1.8: kategoria „Aktywna rozrywka” (escape roomy, laser tag, kręgielnie, parki trampolin, gokarty…)
    /// z domyślnym czasem wizyty 75 min. Bez zmian schematu – tylko dane słownika.
    /// </summary>
    public partial class ActiveEntertainmentCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                insert into catalog.categories (code, name, default_visit_minutes)
                values ('active-entertainment', 'Aktywna rozrywka', 75)
                on conflict (code) do nothing;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Usuwamy kategorię tylko, gdy nie ma przypisanych atrakcji – inaczej kaskada skasowałaby przypisania.
            migrationBuilder.Sql("""
                delete from catalog.categories c
                where c.code = 'active-entertainment'
                  and not exists (select 1 from catalog.place_categories pc where pc.category_code = c.code);
                """);
        }
    }
}
