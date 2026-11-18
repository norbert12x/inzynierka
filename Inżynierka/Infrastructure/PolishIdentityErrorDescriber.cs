using Microsoft.AspNetCore.Identity;

namespace Inżynierka.Infrastructure;

// Polskie komunikaty błędów Identity (walidacja hasła, e-maila, ról). Kody (Code) zostają oryginalne –
// AuthController rozpoznaje po nich np. zajęty adres.
public sealed class PolishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError()
        => Error(nameof(DefaultError), "Wystąpił nieznany błąd konta.");

    public override IdentityError ConcurrencyFailure()
        => Error(nameof(ConcurrencyFailure), "Konto zostało w międzyczasie zmienione. Spróbuj ponownie.");

    public override IdentityError PasswordMismatch()
        => Error(nameof(PasswordMismatch), "Nieprawidłowe hasło.");

    public override IdentityError InvalidToken()
        => Error(nameof(InvalidToken), "Nieprawidłowy lub wygasły kod.");

    public override IdentityError LoginAlreadyAssociated()
        => Error(nameof(LoginAlreadyAssociated), "To konto zewnętrzne jest już przypisane do innego użytkownika.");

    public override IdentityError InvalidUserName(string? userName)
        => Error(nameof(InvalidUserName), "Nieprawidłowy adres e-mail.");

    public override IdentityError InvalidEmail(string? email)
        => Error(nameof(InvalidEmail), "Nieprawidłowy adres e-mail.");

    public override IdentityError DuplicateUserName(string userName)
        => Error(nameof(DuplicateUserName), "Konto z tym adresem e-mail już istnieje.");

    public override IdentityError DuplicateEmail(string email)
        => Error(nameof(DuplicateEmail), "Konto z tym adresem e-mail już istnieje.");

    public override IdentityError InvalidRoleName(string? role)
        => Error(nameof(InvalidRoleName), $"Nieprawidłowa nazwa roli „{role}”.");

    public override IdentityError DuplicateRoleName(string role)
        => Error(nameof(DuplicateRoleName), $"Rola „{role}” już istnieje.");

    public override IdentityError UserAlreadyHasPassword()
        => Error(nameof(UserAlreadyHasPassword), "Konto ma już ustawione hasło.");

    public override IdentityError UserLockoutNotEnabled()
        => Error(nameof(UserLockoutNotEnabled), "Blokada nie jest włączona dla tego konta.");

    public override IdentityError UserAlreadyInRole(string role)
        => Error(nameof(UserAlreadyInRole), $"Użytkownik ma już rolę „{role}”.");

    public override IdentityError UserNotInRole(string role)
        => Error(nameof(UserNotInRole), $"Użytkownik nie ma roli „{role}”.");

    public override IdentityError PasswordTooShort(int length)
        => Error(nameof(PasswordTooShort), $"Hasło musi mieć co najmniej {length} znaków.");

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
        => Error(nameof(PasswordRequiresUniqueChars), $"Hasło musi zawierać co najmniej {uniqueChars} różnych znaków.");

    public override IdentityError PasswordRequiresNonAlphanumeric()
        => Error(nameof(PasswordRequiresNonAlphanumeric), "Hasło musi zawierać znak specjalny.");

    public override IdentityError PasswordRequiresDigit()
        => Error(nameof(PasswordRequiresDigit), "Hasło musi zawierać cyfrę.");

    public override IdentityError PasswordRequiresLower()
        => Error(nameof(PasswordRequiresLower), "Hasło musi zawierać małą literę.");

    public override IdentityError PasswordRequiresUpper()
        => Error(nameof(PasswordRequiresUpper), "Hasło musi zawierać wielką literę.");

    public override IdentityError RecoveryCodeRedemptionFailed()
        => Error(nameof(RecoveryCodeRedemptionFailed), "Nieprawidłowy kod odzyskiwania.");

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
