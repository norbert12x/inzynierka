using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Controllers;

// Panel administratora (E2.3, później E6.6). Gość → 401, użytkownik bez roli Admin → 403.
// Rolę Admin nadaje się SQL-em (docs/api.md, sekcja „Konta”).
[ApiController]
[Route("api/admin/users")]
[Authorize(Roles = AppRoles.Admin)]
public sealed class AdminUsersController(AccountsDbContext accounts) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AdminUserItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AdminUserItem>>> List(CancellationToken cancellationToken)
    {
        var items = await accounts.Users
            .AsNoTracking()
            .OrderBy(u => u.CreatedAt)
            .Select(u => new AdminUserItem(
                u.Id,
                u.Email ?? "",
                (from userRole in accounts.UserRoles
                 join role in accounts.Roles on userRole.RoleId equals role.Id
                 where userRole.UserId == u.Id
                 orderby role.Name
                 select role.Name ?? "").ToList(),
                u.CreatedAt,
                u.LockoutEnd))
            .ToListAsync(cancellationToken);
        return Ok(items);
    }
}
