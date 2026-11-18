namespace Inżynierka.Models;

public sealed record AdminUserItem(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LockoutEnd);
