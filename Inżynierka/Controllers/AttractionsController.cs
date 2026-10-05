using System.Text.Json;
using Inżynierka.Data;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/attractions")]
public class AttractionsController(
    OverpassAttractionService service,
    ILogger<AttractionsController> logger) : ControllerBase
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
        // Baza jest opcjonalna w konfiguracji; podgląd Overpass nadal może działać bez niej.
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

    [HttpGet("preview/rzeszow")]
    [ProducesResponseType(typeof(AttractionPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<AttractionPreviewResponse>> PreviewRzeszow(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetRzeszowAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Przekroczono czas oczekiwania na dane atrakcji.",
                detail: "Serwis OpenStreetMap/Overpass nie odpowiedział w ciągu 60 sekund. Spróbuj ponownie później.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Nie udało się pobrać atrakcji z Overpass.");
            var upstreamStatus = (int?)exception.StatusCode;
            var (status, title, detail) = upstreamStatus switch
            {
                504 => (StatusCodes.Status504GatewayTimeout,
                    "Serwer Overpass przekroczył czas obsługi zapytania.",
                    "Dostawca zwrócił HTTP 504. Może być przeciążony. Odczekaj około minuty i ponów próbę."),
                429 => (StatusCodes.Status503ServiceUnavailable,
                    "Serwer Overpass ograniczył liczbę zapytań.",
                    "Dostawca zwrócił HTTP 429. Odczekaj przed kolejną próbą; nie wysyłaj kilku zapytań jednocześnie."),
                502 or 503 => (StatusCodes.Status503ServiceUnavailable,
                    "Serwer Overpass jest chwilowo niedostępny.",
                    $"Dostawca zwrócił HTTP {upstreamStatus}. Spróbuj ponownie później."),
                null => (StatusCodes.Status503ServiceUnavailable,
                    "Nie udało się połączyć z serwerem Overpass.",
                    "Nie otrzymano odpowiedzi HTTP. Sprawdź połączenie z internetem; szczegóły błędu są w logach aplikacji."),
                _ => (StatusCodes.Status502BadGateway,
                    "Serwer Overpass odrzucił zapytanie.",
                    $"Dostawca zwrócił HTTP {upstreamStatus}. Szczegóły błędu są w logach aplikacji.")
            };

            return Problem(statusCode: status, title: title, detail: detail,
                extensions: new Dictionary<string, object?>
                {
                    ["upstreamStatusCode"] = upstreamStatus,
                    ["connectionError"] = upstreamStatus is null ? exception.HttpRequestError.ToString() : null
                });
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            logger.LogWarning(exception, "Niepoprawna odpowiedź Overpass.");
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Źródło danych zwróciło niepoprawną lub niepełną odpowiedź.",
                detail: "Nie prezentujemy częściowych wyników jako kompletnego pobrania. Spróbuj ponownie później.");
        }
    }
}
