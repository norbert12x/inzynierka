using System.ComponentModel.DataAnnotations;

namespace Inżynierka.Models;

public sealed class RegisterRequest
{
    [Required(ErrorMessage = "Podaj adres e-mail.")]
    [EmailAddress(ErrorMessage = "Nieprawidłowy adres e-mail.")]
    [StringLength(256, ErrorMessage = "Adres e-mail może mieć najwyżej 256 znaków.")]
    public string Email { get; set; } = "";

    // Pozostałe reguły hasła (cyfra, mała i wielka litera) sprawdza Identity – opcje w Program.cs.
    [Required(ErrorMessage = "Podaj hasło.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Hasło musi mieć od 8 do 128 znaków.")]
    public string Password { get; set; } = "";
}

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Podaj adres e-mail.")]
    [StringLength(256, ErrorMessage = "Adres e-mail może mieć najwyżej 256 znaków.")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Podaj hasło.")]
    [StringLength(128, ErrorMessage = "Hasło może mieć najwyżej 128 znaków.")]
    public string Password { get; set; } = "";

    // true – ciasteczko trwałe (14 dni, przedłużane); false – do zamknięcia przeglądarki.
    public bool RememberMe { get; set; }
}

public sealed record CurrentUserResponse(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt);
