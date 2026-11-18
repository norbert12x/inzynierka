using System.Text.RegularExpressions;
using Inżynierka.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Data;

// Konta i role (E2) w osobnym schemacie identity; katalog atrakcji zostaje w CatalogDbContext.
// Migracje: Add-Migration <Nazwa> -Context AccountsDbContext -OutputDir Migrations/Accounts
public sealed partial class AccountsDbContext(DbContextOptions<AccountsDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "identity";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        // Nazwy w stylu bazy (snake_case) zamiast domyślnych AspNetUsers / PascalCase.
        builder.Entity<AppUser>().ToTable("users");
        builder.Entity<IdentityRole<Guid>>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.Entity<AppUser>().Property(u => u.CreatedAt).HasDefaultValueSql("now()");

        builder.Entity<IdentityRole<Guid>>().HasData(
            new IdentityRole<Guid>
            {
                Id = AppRoles.UserId,
                Name = AppRoles.User,
                NormalizedName = AppRoles.User.ToUpperInvariant(),
                ConcurrencyStamp = "role-user-v1"
            },
            new IdentityRole<Guid>
            {
                Id = AppRoles.AdminId,
                Name = AppRoles.Admin,
                NormalizedName = AppRoles.Admin.ToUpperInvariant(),
                ConcurrencyStamp = "role-admin-v1"
            });

        foreach (var entity in builder.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string name) => UpperLetter().Replace(name, "_$1").ToLowerInvariant();

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex UpperLetter();
}
