using System.Text.Json;
using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Infrastructure;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Inżynierka.Controllers;

// Dodawanie nowych atrakcji (strona /dev/atrakcje), tylko Development: z wyciągu Overture albo ręcznie,
// z ochroną przed duplikatami. Docelowo przejdzie do panelu administratora (E6.6).
[ApiController]
[DevelopmentOnly]
[Route("api/dev/places")]
public sealed class DevPlacesController(
    CatalogDbContext database,
    OvertureCandidatesStore overture,
    ILogger<DevPlacesController> logger) : ControllerBase
{
    // Dane źródłowe w tym samym kształcie co import Geoapify (sourceDetails.raw).
    private static readonly string ManualRawData =
        JsonSerializer.Serialize(new { sourceDetails = new { sourcename = SourceProviders.Manual, raw = new { } } });

    // Kandydaci z ostatniego wyciągu Overture (scripts/fetch_overture_places.py), z oceną duplikatów.
    [HttpGet("overture-candidates")]
    [ProducesResponseType(typeof(OvertureCandidatesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OvertureCandidatesResponse>> OvertureCandidates(CancellationToken cancellationToken)
    {
        var file = await overture.LoadLatestAsync(cancellationToken);
        if (file is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Brak wyciągu Overture.",
                detail: "Uruchom: python scripts/fetch_overture_places.py (zapisuje data/overture/<wydanie>/candidates.json).");

        var existing = await LoadExistingAsync(cancellationToken);
        var items = file.Items
            .Select(c => new OvertureCandidateItem(c,
                PlaceDuplicates.Find(c.Name, c.Latitude, c.Longitude, PlaceDuplicates.OvertureKey(c.Id), existing)))
            .ToList();
        return Ok(new OvertureCandidatesResponse(file.Release, file.FetchedAtUtc, file.LicenseInfo, items.Count, items));
    }

    [HttpPost("duplicates")]
    [ProducesResponseType(typeof(IReadOnlyList<DuplicateMatch>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IReadOnlyList<DuplicateMatch>> Duplicates([FromBody] DuplicateCheckRequest request,
        CancellationToken cancellationToken)
        => PlaceDuplicates.Find(request.Name.Trim(), request.Latitude, request.Longitude,
            request.OvertureId is null ? null : PlaceDuplicates.OvertureKey(request.OvertureId),
            await LoadExistingAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(CreatePlaceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(DuplicateConflict), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatePlaceResponse>> Create([FromBody] CreatePlaceRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var codes = request.CategoryCodes.Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList();
        var known = await database.Categories.Where(c => codes.Contains(c.Code)).Select(c => c.Code).ToListAsync(cancellationToken);
        var unknown = codes.Except(known).ToList();
        if (unknown.Count > 0)
        {
            ModelState.AddModelError(nameof(request.CategoryCodes), $"Nieznane kategorie: {string.Join(", ", unknown)}.");
            return ValidationProblem(ModelState);
        }

        // Dane z Overture bierzemy z wyciągu na dysku, nie z żądania (źródło i licencja muszą się zgadzać).
        OvertureCandidatesFile? file = null;
        OvertureCandidate? candidate = null;
        if (request.OvertureId is not null)
        {
            file = await overture.LoadLatestAsync(cancellationToken);
            candidate = file?.Items.FirstOrDefault(c => c.Id == request.OvertureId);
            if (candidate is null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie ma takiego miejsca w wyciągu Overture.");
        }

        // Ponowne sprawdzenie po stronie serwera – strona mogła pokazać nieaktualny stan.
        var sourceKey = candidate is null ? null : PlaceDuplicates.OvertureKey(candidate.Id);
        var matches = PlaceDuplicates.Find(name, request.Latitude, request.Longitude, sourceKey,
            await LoadExistingAsync(cancellationToken));
        if (matches.Any(m => m.Level == PlaceDuplicates.SameSource))
            return Conflict(new DuplicateConflict("Ten obiekt już jest w katalogu (to samo źródło).", false, matches));
        if (matches.Any(m => m.Level == PlaceDuplicates.Likely) && !request.ConfirmDuplicate)
            return Conflict(new DuplicateConflict("Możliwy duplikat – sprawdź i potwierdź, jeśli to inne miejsce.", true, matches));

        var place = new CatalogPlace
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            City = Clean(request.City),
            Address = Clean(request.Address),
            Location = new Point(request.Longitude, request.Latitude) { SRID = 4326 },
            OpeningHours = Clean(request.OpeningHours),
            Website = Clean(request.Website),
            EstimatedVisitMinutes = request.EstimatedVisitMinutes,
            ManuallyEditedFields = candidate is null
                // Ręcznie dodane – wszystkie pola są decyzją użytkownika.
                ? ["name", "city", "address", "location", "categories", "website", "opening_hours", "estimated_visit_minutes"]
                // Z Overture – ręcznie wybrane są kategorie (i ewentualnie czas wizyty).
                : request.EstimatedVisitMinutes is null ? ["categories"] : ["categories", "estimated_visit_minutes"]
        };
        foreach (var code in codes)
            place.Categories.Add(new PlaceCategory { PlaceId = place.Id, CategoryCode = code });
        var source = new PlaceSource
        {
            Id = Guid.CreateVersion7(),
            PlaceId = place.Id,
            Provider = candidate is null ? SourceProviders.Manual : SourceProviders.Overture,
            ExternalId = candidate?.Id ?? place.Id.ToString(),
            FetchedAtUtc = DateTimeOffset.UtcNow,
            RawData = candidate is null ? ManualRawData : OvertureRawData(candidate, file!.Release)
        };
        place.Sources.Add(source);

        database.Places.Add(place);
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Dodano atrakcję {PlaceId} ({Source}).", place.Id, source.Provider);
        return StatusCode(StatusCodes.Status201Created, new CreatePlaceResponse(place.Id, place.Name, source.Provider, matches));
    }

    // Wszystkie miejsca katalogu (każdy status i rodzaj) z kluczami źródeł: OSM z importu Geoapify, identyfikator Overture.
    private async Task<List<ExistingPlace>> LoadExistingAsync(CancellationToken cancellationToken)
    {
        var rows = await database.Places.AsNoTracking()
            .Select(p => new
            {
                p.Id, p.Name, p.Status, p.Location,
                Sources = p.Sources.Select(s => new { s.Provider, s.ExternalId, s.RawData }).ToList()
            })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ExistingPlace(r.Id, r.Name, r.Status, r.Location.Y, r.Location.X,
                r.Sources.Select(s => s.Provider == SourceProviders.Overture
                        ? PlaceDuplicates.OvertureKey(s.ExternalId)
                        : PlaceDuplicates.OsmKeyFromRawData(s.RawData))
                    .OfType<string>().ToHashSet()))
            .ToList();
    }

    // Overture: oznaczenie licencji per źródło (Meta, Microsoft, Foursquare…) i cały rekord wyciągu.
    private static string OvertureRawData(OvertureCandidate candidate, string release)
        => JsonSerializer.Serialize(new
        {
            sourceDetails = new
            {
                sourcename = SourceProviders.Overture,
                release,
                attribution = candidate.Attribution,
                license = string.Join(", ", candidate.Sources.Select(s => s.License).OfType<string>().Distinct()),
                url = "https://docs.overturemaps.org/attribution/",
                raw = candidate
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
