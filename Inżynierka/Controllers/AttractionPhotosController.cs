using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/attractions/{attractionId:guid}/photos")]
public sealed class AttractionPhotosController(IWebHostEnvironment environment,
    ILogger<AttractionPhotosController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AttractionPhotosResponse), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<AttractionPhotosResponse>> List(Guid attractionId,
        [FromServices] IServiceProvider services, CancellationToken cancellationToken,
        [FromQuery] bool includePending = false)
    {
        var database = services.GetService<CatalogDbContext>();
        if (database is null) return Problem(statusCode: 503, title: "Brak konfiguracji bazy.");
        // Propozycje do przeglądu tylko w Development; poza nim parametr jest ignorowany.
        var showPending = includePending && environment.IsDevelopment();
        try
        {
            var exists = await database.Places.AnyAsync(p => p.Id == attractionId && p.Kind == "attraction"
                && (p.Status == "active" || showPending && p.Status == "awaiting_photo"), cancellationToken);
            if (!exists) return Problem(statusCode: 404, title: "Nie znaleziono atrakcji.");
            var rows = await database.Photos.AsNoTracking().Where(p => p.PlaceId == attractionId
                && (p.Status == "approved" || showPending && p.Status == "pending_review"))
                // Najpierw zdjęcia miejsca, potem eksponaty.
                .OrderBy(p => p.Kind == PhotoKinds.Exhibit).ThenBy(p => p.SortOrder).ThenBy(p => p.FetchedAtUtc).ThenBy(p => p.Id)
                .Select(p => new { p.Id, p.Author, p.Credit, p.License, p.LicenseUrl, p.SourcePageUrl, p.Status, p.Kind, p.Caption })
                .ToListAsync(cancellationToken);
            var items = rows.Select(p => new AttractionPhoto(p.Id,
                $"/api/attractions/{attractionId}/photos/{p.Id}/image" + (showPending ? "?includePending=true" : ""),
                p.Author, p.Credit, p.License, p.LicenseUrl, p.SourcePageUrl, p.Status, p.Kind, p.Caption)).ToList();
            return Ok(new AttractionPhotosResponse(attractionId, items));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception, cancellationToken))
        {
            logger.LogWarning("Nie udało się odczytać zdjęć ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: 503, title: "Nie udało się pobrać zdjęć z bazy.");
        }
    }

    [HttpGet("{photoId:guid}/image")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<IActionResult> Image(Guid attractionId, Guid photoId,
        [FromServices] IServiceProvider services, CancellationToken cancellationToken,
        [FromQuery] bool includePending = false)
    {
        var database = services.GetService<CatalogDbContext>();
        if (database is null) return Problem(statusCode: 503, title: "Brak konfiguracji bazy.");
        var showPending = includePending && environment.IsDevelopment();
        try
        {
            var photo = await database.Photos.AsNoTracking().Where(p => p.Id == photoId && p.PlaceId == attractionId
                && p.Place.Kind == "attraction" && (p.Place.Status == "active"
                    || showPending && p.Place.Status == "awaiting_photo")
                && (p.Status == "approved" || showPending && p.Status == "pending_review"))
                .Select(p => new { p.CachedRelativePath }).SingleOrDefaultAsync(cancellationToken);
            if (photo is null) return Problem(statusCode: 404, title: "Nie znaleziono dostępnego zdjęcia.");
            var path = PhotoCache.ResolveFile(environment.ContentRootPath, photo.CachedRelativePath);
            if (path is null)
                return Problem(statusCode: 404, title: "Nieprawidłowa lokalizacja zdjęcia.");
            var contentType = PhotoCache.ContentType(path);
            if (contentType is null || !System.IO.File.Exists(path))
                return Problem(statusCode: 404, title: "Plik zdjęcia jest niedostępny.");
            Response.Headers.CacheControl = "no-store";
            return PhysicalFile(path, contentType);
        }
        catch (Exception exception) when (IsDatabaseFailure(exception, cancellationToken))
        {
            logger.LogWarning("Nie udało się odczytać pliku zdjęcia ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: 503, title: "Nie udało się pobrać zdjęcia z bazy.");
        }
    }

    // Błąd bazy albo przekroczony czas zapytania (anulowanie, którego nie zażądał klient) → 503 zamiast 500.
    private static bool IsDatabaseFailure(Exception exception, CancellationToken cancellationToken)
        => exception is Npgsql.NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
}
