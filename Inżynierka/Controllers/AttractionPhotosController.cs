using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Controllers;

// Zdjęcia atrakcji: lista zatwierdzonych i plik obrazu z .local/photo-cache.
// W Development ?includePending=true pokazuje też propozycje (i murale czekające na zdjęcie) – dla strony /dev/zdjecia.
[ApiController]
[Route("api/attractions/{attractionId:guid}/photos")]
public sealed class AttractionPhotosController(CatalogDbContext database, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AttractionPhotosResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AttractionPhotosResponse>> List(Guid attractionId, CancellationToken cancellationToken,
        [FromQuery] bool includePending = false)
    {
        var showPending = ShowPending(includePending);
        var exists = await database.Places.AnyAsync(p => p.Id == attractionId && p.Kind == PlaceKinds.Attraction
            && (p.Status == PlaceStatuses.Active || showPending && p.Status == PlaceStatuses.AwaitingPhoto), cancellationToken);
        if (!exists)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono atrakcji.");

        var items = await database.Photos.AsNoTracking()
            .Where(p => p.PlaceId == attractionId
                && (p.Status == PhotoStatuses.Approved || showPending && p.Status == PhotoStatuses.PendingReview))
            // Najpierw zdjęcia miejsca, potem eksponaty; w galerii według sort_order.
            .OrderBy(p => p.Kind == PhotoKinds.Exhibit).ThenBy(p => p.SortOrder).ThenBy(p => p.FetchedAtUtc).ThenBy(p => p.Id)
            .Select(p => new AttractionPhoto(p.Id, "", p.Author, p.Credit, p.License, p.LicenseUrl, p.SourcePageUrl,
                p.Status, p.Kind, p.Caption))
            .ToListAsync(cancellationToken);
        return Ok(new AttractionPhotosResponse(attractionId,
            items.Select(p => p with { ImageUrl = PhotoCache.ImageUrl(attractionId, p.Id, showPending) }).ToList()));
    }

    [HttpGet("{photoId:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Image(Guid attractionId, Guid photoId, CancellationToken cancellationToken,
        [FromQuery] bool includePending = false)
    {
        var showPending = ShowPending(includePending);
        var relativePath = await database.Photos.AsNoTracking()
            .Where(p => p.Id == photoId && p.PlaceId == attractionId && p.Place.Kind == PlaceKinds.Attraction
                && (p.Place.Status == PlaceStatuses.Active || showPending && p.Place.Status == PlaceStatuses.AwaitingPhoto)
                && (p.Status == PhotoStatuses.Approved || showPending && p.Status == PhotoStatuses.PendingReview))
            .Select(p => p.CachedRelativePath)
            .SingleOrDefaultAsync(cancellationToken);
        if (relativePath is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono dostępnego zdjęcia.");

        var path = PhotoCache.ResolveFile(environment.ContentRootPath, relativePath);
        var contentType = path is null ? null : PhotoCache.ContentType(path);
        if (path is null || contentType is null || !System.IO.File.Exists(path))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Plik zdjęcia jest niedostępny.");
        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(path, contentType);
    }

    // Propozycje do przeglądu tylko w Development; poza nim parametr jest ignorowany.
    private bool ShowPending(bool includePending) => includePending && environment.IsDevelopment();
}
