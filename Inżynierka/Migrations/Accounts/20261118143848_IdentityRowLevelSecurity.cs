using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inżynierka.Migrations.Accounts
{
    /// <summary>
    /// Zadanie 2.1: RLS dla tabel kont i brak uprawnień dla ról Supabase (anon, authenticated).
    /// Backend łączy się bezpośrednio z PostgreSQL, więc polityk RLS nie dodajemy – bez nich nikt spoza
    /// właściciela tabel nie odczyta danych (np. hashy haseł) przez Data API.
    /// </summary>
    public partial class IdentityRowLevelSecurity : Migration
    {
        private static readonly string[] Tables =
        [
            "users", "roles", "user_roles", "user_claims", "user_logins", "user_tokens", "role_claims",
            "__ef_migrations_history"
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
                migrationBuilder.Sql($"alter table identity.{table} enable row level security;");

            migrationBuilder.Sql("""
                revoke all on schema identity from public, anon, authenticated;
                revoke all on all tables in schema identity from public, anon, authenticated;
                revoke all on all sequences in schema identity from public, anon, authenticated;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Uprawnień nie przywracamy – wcześniej ich nie nadawaliśmy.
            foreach (var table in Tables)
                migrationBuilder.Sql($"alter table identity.{table} disable row level security;");
        }
    }
}
