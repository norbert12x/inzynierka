using Inżynierka.Data;
using Inżynierka.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Controllers;

// Słownik kategorii z bazy (catalog.categories) – jedno źródło dla filtrów i czasów wizyt.
[ApiController]
[Route("api/attraction-categories")]
public class AttractionCategoriesController(CatalogDbContext database) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AttractionCategoryInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IReadOnlyList<AttractionCategoryInfo>> Get(CancellationToken cancellationToken)
        => await database.Categories.AsNoTracking()
            .OrderBy(c => c.Code == "other").ThenBy(c => c.Name)
            .Select(c => new AttractionCategoryInfo(c.Code, c.Name, c.DefaultVisitMinutes))
            .ToListAsync(cancellationToken);
}
