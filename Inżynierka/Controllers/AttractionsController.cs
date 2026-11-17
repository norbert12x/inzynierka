using Inżynierka.Data;
using Inżynierka.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/attractions")]
public class AttractionsController(ILogger<AttractionsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AttractionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AttractionListResponse>> List(
        [FromQuery] AttractionListQuery parameters,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Bez konfiguracji bazy aplikacja startuje (Swagger, obszary), a katalog zwraca 503.
        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Brak konfiguracji bazy katalogu.",
                detail: "Skonfiguruj połączenie z Supabase i uruchom aplikację ponownie.");

        var category = parameters.Category?.Trim().ToLowerInvariant();
        var search = parameters.Search?.Trim();
        var city = parameters.City?.Trim().ToLowerInvariant();
        var center = parameters.Latitude.HasValue && parameters.Longitude.HasValue
            ? new Point(parameters.Longitude.Value, parameters.Latitude.Value) { SRID = 4326 }
            : null;

        try
        {
            var query = database.Places.AsNoTracking()
                .Where(place => place.Kind == "attraction" && place.Status == "active");
            if (parameters.RequirePhoto)
                query = query.Where(place => place.Photos.Any(photo => photo.Status == "approved"));

            if (!string.IsNullOrEmpty(city))
                query = query.Where(place => place.City != null && place.City.Trim().ToLower() == city);

            if (center is not null)
            {
                var radiusMeters = parameters.RadiusKm!.Value * 1000;
                query = query.Where(place => EF.Functions.IsWithinDistance(place.Location, center, radiusMeters, true));
            }

            if (!string.IsNullOrEmpty(category))
            {
                if (!await database.Categories.AnyAsync(item => item.Code == category, cancellationToken))
                {
                    ModelState.AddModelError(nameof(parameters.Category),
                        "Nieznana kategoria. Dostępne kody znajdziesz w GET /api/attraction-categories.");
                    return ValidationProblem(ModelState);
                }

                query = query.Where(place => place.Categories.Any(item => item.CategoryCode == category));
            }

            if (!string.IsNullOrEmpty(search))
            {
                // Znaki %, _ i backslash traktujemy jako tekst, nie wzorce SQL.
                var escapedSearch = search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var pattern = $"%{escapedSearch}%";
                query = query.Where(place => EF.Functions.ILike(place.Name, pattern, "\\"));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var ordered = center is null
                ? query.OrderBy(place => place.Name).ThenBy(place => place.Id)
                : query.OrderBy(place => EF.Functions.Distance(place.Location, center, true))
                    .ThenBy(place => place.Name).ThenBy(place => place.Id);
            var rows = await ordered
                .Skip((parameters.Page - 1) * parameters.PageSize)
                .Take(parameters.PageSize)
                .Select(place => new
                {
                    place.Id,
                    place.Name,
                    place.City,
                    place.Address,
                    place.Location,
                    HasPhoto = place.Photos.Any(photo => photo.Status == "approved"),
                    PendingPhotoCount = place.Photos.Count(photo => photo.Status == "pending_review"),
                    DistanceKm = center == null ? (double?)null : EF.Functions.Distance(place.Location, center, true) / 1000,
                    Categories = place.Categories.OrderBy(item => item.CategoryCode)
                        .Select(item => new AttractionCategory(item.CategoryCode, item.Category.Name)).ToList(),
                    place.OpeningHours,
                    place.OpeningHoursVerification,
                    place.OpeningHoursConfirmedOn
                })
                .ToListAsync(cancellationToken);

            // Odczyt współrzędnych po pobraniu punktu geography; X = długość, Y = szerokość.
            var items = rows.Select(place => new AttractionListItem(
                place.Id, place.Name, place.City, place.Address, place.Location.Y, place.Location.X,
                place.Categories, place.OpeningHours, place.OpeningHoursVerification,
                place.OpeningHoursConfirmedOn, place.DistanceKm, place.HasPhoto, place.PendingPhotoCount)).ToList();

            return Ok(new AttractionListResponse(items, totalCount, parameters.Page, parameters.PageSize,
                (int)Math.Ceiling(totalCount / (double)parameters.PageSize)));
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Odczyt listy atrakcji nie powiódł się ({ExceptionType}, SQLSTATE={SqlState}).",
                exception.GetType().Name, (exception as Npgsql.PostgresException)?.SqlState);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Nie udało się pobrać atrakcji z bazy.",
                detail: "Sprawdź połączenie z Supabase i spróbuj ponownie później.");
        }
    }

    // Tylko aktywne atrakcje; usunięte, murale czekające na zdjęcie i elementy obiektów zwracają 404.
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AttractionDetails), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AttractionDetails>> Details(
        Guid id,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Brak konfiguracji bazy katalogu.",
                detail: "Skonfiguruj połączenie z Supabase i uruchom aplikację ponownie.");

        try
        {
            var place = await database.Places.AsNoTracking().AsSplitQuery()
                .Where(p => p.Id == id && p.Kind == "attraction" && p.Status == "active")
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    p.City,
                    p.Address,
                    p.Location,
                    p.Website,
                    Categories = p.Categories.OrderBy(item => item.CategoryCode)
                        .Select(item => new AttractionCategory(item.CategoryCode, item.Category.Name)).ToList(),
                    p.OpeningHours,
                    p.OpeningHoursVerification,
                    p.OpeningHoursConfirmedOn,
                    p.EstimatedVisitMinutes,
                    Components = p.Components.Where(c => c.Status == "component")
                        .OrderBy(c => c.Name).ThenBy(c => c.Id)
                        .Select(c => new AttractionComponent(c.Id, c.Name)).ToList(),
                    Photos = p.Photos.Where(photo => photo.Status == "approved")
                        .OrderBy(photo => photo.FetchedAtUtc).ThenBy(photo => photo.Id)
                        .Select(photo => new
                        {
                            photo.Id, photo.Author, photo.Credit, photo.License,
                            photo.LicenseUrl, photo.SourcePageUrl, photo.Status
                        }).ToList(),
                    FromGeoapify = p.Sources.Any(s => s.Provider == "geoapify")
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (place is null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono atrakcji.",
                    detail: "Atrakcja nie istnieje albo nie jest obecnie dostępna w katalogu.");

            var photos = place.Photos.Select(photo => new AttractionPhoto(photo.Id,
                $"/api/attractions/{place.Id}/photos/{photo.Id}/image",
                photo.Author, photo.Credit, photo.License, photo.LicenseUrl, photo.SourcePageUrl, photo.Status)).ToList();

            // X = długość, Y = szerokość geograficzna.
            return Ok(new AttractionDetails(
                place.Id, place.Name, place.Description, place.City, place.Address,
                place.Location.Y, place.Location.X, place.Website, place.Categories,
                place.OpeningHours, place.OpeningHoursVerification, place.OpeningHoursConfirmedOn,
                place.EstimatedVisitMinutes, place.Components, photos,
                place.FromGeoapify ? GeoapifyAttribution : null));
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Odczyt szczegółów atrakcji nie powiódł się ({ExceptionType}, SQLSTATE={SqlState}).",
                exception.GetType().Name, (exception as Npgsql.PostgresException)?.SqlState);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Nie udało się pobrać atrakcji z bazy.",
                detail: "Sprawdź połączenie z Supabase i spróbuj ponownie później.");
        }
    }

    private const string GeoapifyAttribution = "Powered by Geoapify | © OpenStreetMap contributors";
}
