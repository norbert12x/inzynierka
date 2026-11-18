using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Inżynierka.Controllers;

// Konta: e-mail + hasło, sesja w ciasteczku HttpOnly (konfiguracja w Program.cs, AddAccounts).
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    AccountsDbContext accounts) : ControllerBase
{
    private const string InvalidCredentials = "Nieprawidłowy e-mail lub hasło.";

    // Zakłada konto z rolą User. Nie loguje – po rejestracji wywołaj /login.
    [HttpPost("register")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CurrentUserResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = new AppUser { UserName = email, Email = email };

        // Konto i rola razem – bez roli konto nie powinno powstać.
        await using var transaction = await accounts.Database.BeginTransactionAsync(cancellationToken);
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            return RegistrationFailed(created);
        var roleAdded = await users.AddToRoleAsync(user, AppRoles.User);
        if (!roleAdded.Succeeded)
            return RegistrationFailed(roleAdded);
        await transaction.CommitAsync(cancellationToken);

        return CreatedAtAction(nameof(Me), null, await ToResponseAsync(user));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<CurrentUserResponse>> Login(LoginRequest request)
    {
        // Ten sam komunikat dla nieistniejącego konta i złego hasła – nie zdradzamy, czy adres jest zarejestrowany.
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentials);

        var result = await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return Problem(statusCode: StatusCodes.Status429TooManyRequests,
                title: "Zbyt wiele nieudanych prób logowania.",
                detail: "Konto jest chwilowo zablokowane. Spróbuj ponownie za kilkanaście minut.");
        if (!result.Succeeded)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: InvalidCredentials);

        return Ok(await ToResponseAsync(user));
    }

    // Usuwa ciasteczko. Dla gościa też 204 – wylogowanie jest idempotentne.
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        // Ciasteczko może wskazywać konto usunięte w międzyczasie.
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            await signIn.SignOutAsync();
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Nie jesteś zalogowany.");
        }

        return Ok(await ToResponseAsync(user));
    }

    private async Task<CurrentUserResponse> ToResponseAsync(AppUser user)
        => new(user.Id, user.Email ?? "", (await users.GetRolesAsync(user)).Order().ToList(), user.CreatedAt);

    private ActionResult RegistrationFailed(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                or nameof(IdentityErrorDescriber.DuplicateUserName)))
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Konto z tym adresem e-mail już istnieje.");

        foreach (var error in result.Errors)
        {
            var field = error.Code.StartsWith("Password", StringComparison.Ordinal)
                ? nameof(RegisterRequest.Password)
                : nameof(RegisterRequest.Email);
            ModelState.AddModelError(field, error.Description);
        }
        return ValidationProblem(ModelState);
    }
}
