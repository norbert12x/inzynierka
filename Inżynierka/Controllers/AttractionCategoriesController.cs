using Inżynierka.Data;
using Inżynierka.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Controllers;

// Słownik kategorii czytany z bazy (catalog.categories) – jedno źródło dla filtrów i czasów wizyt.
[ApiController]
[Route("api/attraction-categories")]
public class AttractionCategoriesController(ILogger<AttractionCategoriesController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AttractionCategoryInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<AttractionCategoryInfo>>> Get(
        [FromServices] IServiceProvider services, CancellationToken cancellationToken)
    {
        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Brak konfiguracji bazy katalogu.",
                detail: "Skonfiguruj połączenie z Supabase i uruchom aplikację ponownie.");

        try
        {
            var items = await database.Categories.AsNoTracking()
                .OrderBy(c => c.Code == "other").ThenBy(c => c.Name)
                .Select(c => new AttractionCategoryInfo(c.Code, c.Name, c.DefaultVisitMinutes))
                .ToListAsync(cancellationToken);
            return Ok(items);
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Odczyt kategorii nie powiódł się ({ExceptionType}, SQLSTATE={SqlState}).",
                exception.GetType().Name, (exception as Npgsql.PostgresException)?.SqlState);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Nie udało się pobrać kategorii z bazy.",
                detail: "Sprawdź połączenie z Supabase i wykonanie database/005_category_visit_minutes.sql.");
        }
    }
}
