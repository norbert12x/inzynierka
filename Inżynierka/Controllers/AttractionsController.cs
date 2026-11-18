using System.Text.Json;
using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Models;
using Inżynierka.Planning;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Inżynierka.Controllers;

// Publiczny katalog: tylko aktywne atrakcje. Błędy bazy → 503 (DatabaseExceptionHandler).
[ApiController]
[Route("api/attractions")]
public class AttractionsController(CatalogDbContext database) : ControllerBase
{
    private const string GeoapifyAttribution = "Powered by Geoapify | © OpenStreetMap contributors";

    [HttpGet]
    [ProducesResponseType(typeof(AttractionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AttractionListResponse>> List(
        [FromQuery] AttractionListQuery parameters, CancellationToken cancellationToken)
    {
        var category = parameters.Category?.Trim().ToLowerInvariant();
        var search = parameters.Search?.Trim();
        var city = parameters.City?.Trim().ToLowerInvariant();
        var center = parameters.Latitude.HasValue && parameters.Longitude.HasValue
            ? new Point(parameters.Longitude.Value, parameters.Latitude.Value) { SRID = 4326 }
            : null;

        var query = database.Places.AsNoTracking().PublicAttractions();
        if (parameters.RequirePhoto)
            query = query.Where(p => p.Photos.Any(ph => ph.Status == PhotoStatuses.Approved && ph.Kind == PhotoKinds.Place));
        if (!string.IsNullOrEmpty(city))
            query = query.Where(p => p.City != null && p.City.Trim().ToLower() == city);
        if (center is not null)
            query = query.Where(p => EF.Functions.IsWithinDistance(p.Location, center, parameters.RadiusKm!.Value * 1000, true));
        if (!string.IsNullOrEmpty(category))
        {
            if (!await database.Categories.AnyAsync(c => c.Code == category, cancellationToken))
            {
                ModelState.AddModelError(nameof(parameters.Category),
                    "Nieznana kategoria. Dostępne kody znajdziesz w GET /api/attraction-categories.");
                return ValidationProblem(ModelState);
            }
            query = query.Where(p => p.Categories.Any(c => c.CategoryCode == category));
        }
        if (!string.IsNullOrEmpty(search))
        {
            // Znaki %, _ i backslash traktujemy jako tekst, nie wzorce SQL.
            var pattern = $"%{search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern, "\\"));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var ordered = center is null
            ? query.OrderBy(p => p.Name).ThenBy(p => p.Id)
            : query.OrderBy(p => EF.Functions.Distance(p.Location, center, true)).ThenBy(p => p.Name).ThenBy(p => p.Id);
        var rows = await ordered
            .Skip((parameters.Page - 1) * parameters.PageSize)
            .Take(parameters.PageSize)
            .Select(p => new
            {
                p.Id, p.Name, p.City, p.Address, p.Location,
                // Zdjęcie samej atrakcji – eksponat nie liczy się jako zdjęcie miejsca.
                HasPhoto = p.Photos.Any(ph => ph.Status == PhotoStatuses.Approved && ph.Kind == PhotoKinds.Place),
                PendingPhotoCount = p.Photos.Count(ph => ph.Status == PhotoStatuses.PendingReview),
                DistanceKm = center == null ? (double?)null : EF.Functions.Distance(p.Location, center, true) / 1000,
                Categories = p.Categories.OrderBy(c => c.CategoryCode)
                    .Select(c => new CategoryRow(c.CategoryCode, c.Category.Name, c.Category.DefaultVisitMinutes)).ToList(),
                p.OpeningHours, p.OpeningHoursVerification, p.OpeningHoursConfirmedOn, p.EstimatedVisitMinutes
            })
            .ToListAsync(cancellationToken);

        // X = długość, Y = szerokość geograficzna.
        var items = rows.Select(p =>
        {
            var visit = Visit(p.EstimatedVisitMinutes, p.Categories);
            return new AttractionListItem(p.Id, p.Name, p.City, p.Address, p.Location.Y, p.Location.X,
                Categories(p.Categories), p.OpeningHours, p.OpeningHoursVerification, p.OpeningHoursConfirmedOn,
                visit.Minutes, visit.Source, p.DistanceKm, p.HasPhoto, p.PendingPhotoCount);
        }).ToList();

        return Ok(new AttractionListResponse(items, totalCount, parameters.Page, parameters.PageSize,
            (int)Math.Ceiling(totalCount / (double)parameters.PageSize)));
    }

    // Tylko aktywne atrakcje; usunięte, murale czekające na zdjęcie i elementy obiektów zwracają 404.
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AttractionDetails), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AttractionDetails>> Details(Guid id, CancellationToken cancellationToken)
    {
        var place = await database.Places.AsNoTracking().AsSplitQuery().PublicAttractions()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Name, p.Description, p.City, p.Address, p.Location, p.Website,
                Categories = p.Categories.OrderBy(c => c.CategoryCode)
                    .Select(c => new CategoryRow(c.CategoryCode, c.Category.Name, c.Category.DefaultVisitMinutes)).ToList(),
                p.OpeningHours, p.OpeningHoursVerification, p.OpeningHoursConfirmedOn, p.EstimatedVisitMinutes,
                Components = p.Components.Where(c => c.Status == PlaceStatuses.Component)
                    .OrderBy(c => c.Name).ThenBy(c => c.Id)
                    .Select(c => new AttractionComponent(c.Id, c.Name)).ToList(),
                // Kolejność galerii: pierwsze = zdjęcie główne (ustawiane na stronie /dev/zdjecia).
                Photos = p.Photos.Where(ph => ph.Status == PhotoStatuses.Approved)
                    .OrderBy(ph => ph.SortOrder).ThenBy(ph => ph.FetchedAtUtc).ThenBy(ph => ph.Id)
                    .Select(ph => new AttractionPhoto(ph.Id, "", ph.Author, ph.Credit, ph.License, ph.LicenseUrl,
                        ph.SourcePageUrl, ph.Status, ph.Kind, ph.Caption)).ToList(),
                Sources = p.Sources.Select(s => new { s.Provider, s.RawData }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (place is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono atrakcji.",
                detail: "Atrakcja nie istnieje albo nie jest obecnie dostępna w katalogu.");

        var photos = place.Photos.Select(ph => ph with { ImageUrl = PhotoCache.ImageUrl(place.Id, ph.Id) }).ToList();
        var visit = Visit(place.EstimatedVisitMinutes, place.Categories);
        var attribution = place.Sources.Select(s => Attribution(s.Provider, s.RawData)).FirstOrDefault(a => a is not null);

        return Ok(new AttractionDetails(
            place.Id, place.Name, place.Description, place.City, place.Address, place.Location.Y, place.Location.X,
            place.Website, Categories(place.Categories), place.OpeningHours, place.OpeningHoursVerification,
            place.OpeningHoursConfirmedOn, visit.Minutes, visit.Source, place.Components,
            // Osobno zdjęcia miejsca i eksponaty (galeria muzeum).
            photos.Where(ph => ph.Kind != PhotoKinds.Exhibit).ToList(),
            photos.Where(ph => ph.Kind == PhotoKinds.Exhibit).ToList(),
            attribution));
    }

    private sealed record CategoryRow(string Code, string Name, int DefaultVisitMinutes);

    private static List<AttractionCategory> Categories(List<CategoryRow> rows)
        => rows.Select(c => new AttractionCategory(c.Code, c.Name)).ToList();

    // Każda atrakcja ma co najmniej jedną kategorię (import przypisuje „other”); 30 min to zabezpieczenie.
    private static VisitDuration Visit(int? placeMinutes, List<CategoryRow> categories)
        => VisitDuration.Resolve(placeMinutes, categories.Select(c => c.DefaultVisitMinutes), fallbackMinutes: 30);

    // Oznaczenie źródła danych: Geoapify/OSM albo Overture z licencją rekordu (zapisaną przy dodaniu).
    private static string? Attribution(string provider, string rawData) => provider switch
    {
        SourceProviders.Geoapify => GeoapifyAttribution,
        SourceProviders.Overture => "Overture Maps Foundation. " + (ReadAttribution(rawData) ?? "CDLA Permissive 2.0 / Apache 2.0"),
        _ => null
    };

    private static string? ReadAttribution(string rawData)
    {
        try
        {
            using var document = JsonDocument.Parse(rawData);
            return document.RootElement.TryGetProperty("sourceDetails", out var details)
                && details.TryGetProperty("attribution", out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
