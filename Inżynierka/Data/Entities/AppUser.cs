using Microsoft.AspNetCore.Identity;

namespace Inżynierka.Data.Entities;

// Konto użytkownika (ASP.NET Core Identity, klucz Guid). Logowanie e-mailem: UserName = Email.
public sealed class AppUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }
}

public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";

    // Stałe identyfikatory – role są danymi słownikowymi zapisanymi w migracji (HasData).
    public static readonly Guid UserId = new("6f1c2d3e-0a4b-4c5d-8e9f-100000000001");
    public static readonly Guid AdminId = new("6f1c2d3e-0a4b-4c5d-8e9f-100000000002");
}
