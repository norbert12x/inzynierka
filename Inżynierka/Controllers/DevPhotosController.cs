using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Infrastructure;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Controllers;

// Narzędzia redakcyjne zdjęć (strona /dev/zdjecia), tylko Development:
// propozycje z Wikimedia Commons, kolejka przeglądu, decyzje, zdjęcie główne, naprawa brakujących plików.
// Docelowo przejdą do panelu administratora (E6.6).
[ApiController]
[DevelopmentOnly]
[Route("api/dev/photos")]
public sealed class DevPhotosController(
    CatalogDbContext database,
    IWebHostEnvironment environment,
    PhotoDownloadService downloader,
    WikimediaCommonsService commons,
    ILogger<DevPhotosController> logger) : ControllerBase
{
    // Szuka kilku propozycji zdjęć jednej atrakcji i zapisuje je jako pending_review (pliki do .local/photo-cache).
    [HttpPost("~/api/dev/attractions/{attractionId:guid}/photo-candidates")]
    [ProducesResponseType(typeof(PhotoCandidatesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PhotoCandidatesResponse>> Candidates(
        Guid attractionId,
        CancellationToken cancellationToken,
        // Ile nowych propozycji zapisać (1–20).
        [FromQuery] int limit = 5,
        // Promień wyszukiwania zdjęć z geotagiem; 0 = bez. Większy promień = więcej sąsiednich budynków.
        [FromQuery] int radiusMeters = 50,
        // "place" = zdjęcia atrakcji, "exhibit" = wnętrza i eksponaty (podkategorie kolekcji w Commons).
        [FromQuery] string kind = PhotoKinds.Place)
    {
        if (limit is < 1 or > 20)
            ModelState.AddModelError(nameof(limit), "Limit od 1 do 20.");
        if (radiusMeters is < 0 or > 500)
            ModelState.AddModelError(nameof(radiusMeters), "Promień od 0 do 500 m.");
        if (kind is not (PhotoKinds.Place or PhotoKinds.Exhibit))
            ModelState.AddModelError(nameof(kind), "Dozwolone: place, exhibit.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var place = await database.Places.AsNoTracking()
            .Where(p => p.Id == attractionId && p.Kind == PlaceKinds.Attraction && p.Status != PlaceStatuses.Removed)
            .Select(p => new
            {
                p.Id, p.Name, p.City, p.Location,
                Known = p.Photos.Select(ph => ph.SourceFile).ToList(),
                RawData = p.Sources.Select(s => s.RawData).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (place is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono atrakcji.");

        CommonsCandidateSearch search;
        List<CommonsImage> images;
        // Pliki już znane dla atrakcji (także odrzucone) pomijamy – odrzucone zdjęcie nie wraca.
        var known = place.Known.ToHashSet();
        try
        {
            var wikidataId = place.RawData.Select(WikimediaCommonsService.ObjectWikidataId).FirstOrDefault(id => id is not null);
            search = await commons.FindCandidatesAsync(place.Name, place.City, place.Location.Y, place.Location.X,
                wikidataId, kind == PhotoKinds.Exhibit, radiusMeters, cancellationToken);
            var titles = search.Files.Select(f => f.Title).Where(t => !known.Contains(t)).ToList();
            images = (await commons.GetImagesAsync(titles, cancellationToken)).OrderBy(i => titles.IndexOf(i.Title)).ToList();
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Wikimedia Commons niedostępne (HTTP={StatusCode}).", (int?)exception.StatusCode);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Wikimedia Commons jest chwilowo niedostępne.");
        }
        catch (JsonException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Wikimedia Commons zwróciło nieprawidłową odpowiedź.");
        }

        var methods = search.Files.ToDictionary(f => f.Title, f => f.Method);
        var added = new List<PhotoCandidate>();
        var skipped = new List<PhotoCandidateSkip>();
        foreach (var image in images)
        {
            if (added.Count >= limit)
                break;
            var error = WikimediaCommonsService.RejectionReason(image);
            var relative = CachePath(place.Id, image);
            var path = PhotoCache.ResolveFile(environment.ContentRootPath, relative)!;
            if (error is null && !System.IO.File.Exists(path))
            {
                await Task.Delay(400, cancellationToken); // nie obciążamy Wikimedia
                error = await downloader.DownloadAsync(image.ThumbnailUrl, path, image.Mime, cancellationToken);
            }
            if (error is not null)
            {
                skipped.Add(new PhotoCandidateSkip(image.Title, error, image.ThumbnailUrl, image.DescriptionUrl, image.License, image.Author));
                continue;
            }

            var photo = new PlacePhoto
            {
                Id = Guid.CreateVersion7(),
                PlaceId = place.Id,
                Provider = PhotoProviders.WikimediaCommons,
                SourceFile = image.Title,
                SourcePageUrl = image.DescriptionUrl,
                OriginalUrl = image.OriginalUrl,
                ThumbnailUrl = image.ThumbnailUrl,
                CachedRelativePath = relative,
                Author = image.Author,
                Credit = image.Credit,
                License = image.License,
                LicenseUrl = image.LicenseUrl,
                MatchMethod = methods[image.Title],
                Metadata = image.MetadataJson,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Status = PhotoStatuses.PendingReview,
                Kind = kind,
                Caption = string.IsNullOrWhiteSpace(image.Caption) ? null : image.Caption
            };
            database.Photos.Add(photo);
            await database.SaveChangesAsync(cancellationToken);
            added.Add(new PhotoCandidate(photo.Id, PhotoCache.ImageUrl(place.Id, photo.Id, includePending: true),
                image.Title, image.Author, image.License, image.DescriptionUrl, photo.MatchMethod, photo.Kind, photo.Caption));
        }

        logger.LogInformation("Propozycje zdjęć ({Kind}) dla {PlaceId}: dodano {Added}, pominięto {Skipped}.",
            kind, place.Id, added.Count, skipped.Count);
        return Ok(new PhotoCandidatesResponse(place.Id, place.Name, kind, search.Category, search.Note, added, skipped,
            search.Files.Count(f => known.Contains(f.Title))));
    }

    // Dane dla strony przeglądu: propozycje po kolei i wszystkie atrakcje z liczbą zdjęć.
    [HttpGet("queue")]
    [ProducesResponseType(typeof(PhotoReviewQueue), StatusCodes.Status200OK)]
    public async Task<PhotoReviewQueue> Queue(CancellationToken cancellationToken)
    {
        var pendingRows = await database.Photos.AsNoTracking()
            .Where(ph => ph.Status == PhotoStatuses.PendingReview && ph.Place.Kind == PlaceKinds.Attraction
                && (ph.Place.Status == PlaceStatuses.Active || ph.Place.Status == PlaceStatuses.AwaitingPhoto))
            .OrderBy(ph => ph.Place.Name).ThenBy(ph => ph.PlaceId).ThenBy(ph => ph.FetchedAtUtc)
            .Select(ph => new
            {
                ph.Id, ph.PlaceId, PlaceName = ph.Place.Name, ph.Place.City, PlaceStatus = ph.Place.Status,
                ph.Place.Location, ph.Author, ph.License, ph.LicenseUrl, ph.SourcePageUrl, ph.SourceFile,
                ph.MatchMethod, ph.CachedRelativePath, ph.Kind, ph.Caption,
                // Zatwierdzone tego samego rodzaju (zdjęcia miejsca albo eksponaty).
                ApprovedCount = ph.Place.Photos.Count(o => o.Status == PhotoStatuses.Approved && o.Kind == ph.Kind),
                IsMuseum = ph.Place.Categories.Any(c => c.CategoryCode == "museums")
            })
            .ToListAsync(cancellationToken);
        var pending = pendingRows.Select(r => new PendingPhoto(r.Id, r.PlaceId, r.PlaceName, r.City, r.PlaceStatus,
                r.Location.Y, r.Location.X, PhotoCache.ImageUrl(r.PlaceId, r.Id, includePending: true), FileExists(r.CachedRelativePath),
                r.Author, r.License, r.LicenseUrl, r.SourcePageUrl, r.SourceFile, r.MatchMethod, r.ApprovedCount,
                r.Kind, r.Caption, r.IsMuseum))
            .ToList();

        var places = await database.Places.AsNoTracking().ReviewableAttractions()
            .OrderBy(p => p.Name)
            .Select(p => new PlaceForReview(p.Id, p.Name, p.City, p.Status,
                p.Categories.Any(c => c.CategoryCode == "museums"),
                p.Photos.Count(ph => ph.Status == PhotoStatuses.Approved && ph.Kind == PhotoKinds.Place),
                p.Photos.Count(ph => ph.Status == PhotoStatuses.Approved && ph.Kind == PhotoKinds.Exhibit),
                p.Photos.Count(ph => ph.Status == PhotoStatuses.PendingReview)))
            .ToListAsync(cancellationToken);
        return new PhotoReviewQueue(pending, places);
    }

    // Wszystkie zdjęcia jednej atrakcji, niezależnie od statusu – do ponownego przeglądu.
    [HttpGet("~/api/dev/attractions/{attractionId:guid}/photos")]
    [ProducesResponseType(typeof(AttractionPhotoReview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AttractionPhotoReview>> AttractionPhotos(Guid attractionId, CancellationToken cancellationToken)
    {
        var place = await database.Places.AsNoTracking().AsSplitQuery()
            .Where(p => p.Id == attractionId && p.Kind == PlaceKinds.Attraction)
            .Select(p => new
            {
                p.Id, p.Name, p.City, p.Status, p.Location,
                IsMuseum = p.Categories.Any(c => c.CategoryCode == "museums"),
                RawData = p.Sources.Select(s => s.RawData).ToList(),
                Photos = p.Photos
                    .OrderBy(ph => ph.Status == PhotoStatuses.Approved ? 0 : ph.Status == PhotoStatuses.PendingReview ? 1 : 2)
                    .ThenBy(ph => ph.Kind == PhotoKinds.Exhibit).ThenBy(ph => ph.SortOrder)
                    .ThenBy(ph => ph.FetchedAtUtc).ThenBy(ph => ph.Id)
                    .Select(ph => new
                    {
                        ph.Id, ph.Status, ph.Kind, ph.Caption, ph.SortOrder, ph.Provider, ph.Author, ph.License,
                        ph.LicenseUrl, ph.SourcePageUrl, ph.SourceFile, ph.MatchMethod, ph.CachedRelativePath
                    }).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (place is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono atrakcji.");

        // Pierwsze zatwierdzone każdego rodzaju = zdjęcie główne / pierwszy eksponat.
        var primary = place.Photos.Where(ph => ph.Status == PhotoStatuses.Approved)
            .GroupBy(ph => ph.Kind).Select(g => g.First().Id).ToHashSet();
        var photos = place.Photos.Select(ph => new ReviewPhoto(ph.Id, PhotoCache.ImageUrl(place.Id, ph.Id, includePending: true),
                FileExists(ph.CachedRelativePath), ph.Status, ph.Kind, ph.Caption, ph.SortOrder, primary.Contains(ph.Id),
                ph.Provider, ph.Author, ph.License, ph.LicenseUrl, ph.SourcePageUrl, ph.SourceFile, ph.MatchMethod))
            .ToList();
        var wikidata = place.RawData.Select(WikimediaCommonsService.ObjectWikidataId).FirstOrDefault(id => id is not null);
        return Ok(new AttractionPhotoReview(place.Id, place.Name, place.City, place.Status, place.IsMuseum,
            place.Location.Y, place.Location.X, wikidata, photos));
    }

    // Decyzja o zdjęciu: status i/lub rodzaj (zdjęcie miejsca ↔ wnętrze/eksponat).
    [HttpPost("{photoId:guid}/review")]
    [ProducesResponseType(typeof(PhotoReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PhotoReviewResponse>> Review(Guid photoId, [FromBody] PhotoReviewRequest request,
        CancellationToken cancellationToken)
    {
        var photo = await database.Photos.Include(p => p.Place).SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
        if (photo is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono zdjęcia.");

        var movesToGalleryEnd = request.Status == PhotoStatuses.Approved && photo.Status != PhotoStatuses.Approved
            || request.Kind is not null && request.Kind != photo.Kind;
        photo.Kind = request.Kind ?? photo.Kind;
        photo.Status = request.Status ?? photo.Status;

        // Nowo zatwierdzone (albo przeniesione do innej galerii) trafia na koniec – nie zmienia zdjęcia głównego.
        if (photo.Status == PhotoStatuses.Approved && movesToGalleryEnd)
        {
            var last = await database.Photos
                .Where(p => p.PlaceId == photo.PlaceId && p.Kind == photo.Kind && p.Status == PhotoStatuses.Approved && p.Id != photo.Id)
                .MaxAsync(p => (int?)p.SortOrder, cancellationToken);
            photo.SortOrder = (last ?? -1) + 1;
        }

        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(photo));
    }

    // Ustawia zatwierdzone zdjęcie jako pierwsze w swojej galerii (główne zdjęcie atrakcji / pierwszy eksponat).
    [HttpPost("{photoId:guid}/primary")]
    [ProducesResponseType(typeof(PhotoReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PhotoReviewResponse>> MakePrimary(Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await database.Photos.Include(p => p.Place).SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
        if (photo is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono zdjęcia.");
        if (photo.Status != PhotoStatuses.Approved)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Najpierw zatwierdź zdjęcie.");

        // Wybrane = 0, pozostałe zatwierdzone tego rodzaju 1, 2, … w dotychczasowej kolejności.
        var gallery = await database.Photos
            .Where(p => p.PlaceId == photo.PlaceId && p.Kind == photo.Kind && p.Status == PhotoStatuses.Approved && p.Id != photo.Id)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.FetchedAtUtc).ThenBy(p => p.Id)
            .ToListAsync(cancellationToken);
        photo.SortOrder = 0;
        for (var i = 0; i < gallery.Count; i++)
            gallery[i].SortOrder = i + 1;

        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(photo));
    }

    // Uzupełnia brakujące pliki zdjęć Wikimedia z thumbnail_url zapisanego w bazie. Nie zmienia rekordów w bazie.
    [HttpPost("repair")]
    [ProducesResponseType(typeof(PhotoRepairResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PhotoRepairResponse>> Repair(CancellationToken cancellationToken,
        // true = tylko raport braków, bez pobierania.
        [FromQuery] bool dryRun = true,
        [FromQuery] int limit = 50)
    {
        if (limit is < 1 or > 200)
        {
            ModelState.AddModelError(nameof(limit), "Limit od 1 do 200.");
            return ValidationProblem(ModelState);
        }

        var rows = await database.Photos.AsNoTracking()
            .Where(p => p.Status != PhotoStatuses.Rejected)
            .OrderBy(p => p.Place.Name).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.PlaceId, PlaceName = p.Place.Name, p.Provider, p.Status, p.ThumbnailUrl, p.CachedRelativePath })
            .ToListAsync(cancellationToken);

        var items = new List<PhotoRepairItem>();
        int present = 0, missing = 0, downloaded = 0, failed = 0, attempts = 0;
        foreach (var row in rows)
        {
            PhotoRepairItem Item(string outcome, string? detail = null)
                => new(row.Id, row.PlaceId, row.PlaceName, row.Status, outcome, detail);

            var path = PhotoCache.ResolveFile(environment.ContentRootPath, row.CachedRelativePath);
            var contentType = path is null ? null : PhotoCache.ContentType(path);
            if (path is null || contentType is null)
            {
                items.Add(Item("invalid_path", "Ścieżka pliku poza .local/photo-cache albo nieobsługiwany format."));
                continue;
            }
            if (System.IO.File.Exists(path))
            {
                present++;
                continue;
            }

            missing++;
            if (row.Provider != PhotoProviders.WikimediaCommons)
            {
                items.Add(Item("manual_required", "Zdjęcie własne – plik trzeba skopiować do .local/photo-cache ręcznie."));
                continue;
            }
            if (dryRun || attempts++ >= limit)
            {
                items.Add(Item("missing"));
                continue;
            }

            await Task.Delay(400, cancellationToken); // nie obciążamy Wikimedia
            string? error;
            try
            {
                error = await downloader.DownloadAsync(row.ThumbnailUrl, path, contentType, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException
                || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                error = $"Błąd pobierania ({exception.GetType().Name}).";
            }
            if (error is null)
                downloaded++;
            else
                failed++;
            items.Add(error is null ? Item("downloaded") : Item("failed", error));
        }

        logger.LogInformation("Naprawa zdjęć: brakowało {Missing}, pobrano {Downloaded}, błędy {Failed}.", missing, downloaded, failed);
        return Ok(new PhotoRepairResponse(dryRun, rows.Count, present, missing, downloaded, failed,
            items.Count(i => i.Outcome == "missing"), items));
    }

    private static PhotoReviewResponse ToResponse(PlacePhoto photo)
        => new(photo.Id, photo.PlaceId, photo.Place.Name, photo.Status, photo.Kind, photo.SortOrder);

    private bool FileExists(string cachedRelativePath)
        => PhotoCache.ResolveFile(environment.ContentRootPath, cachedRelativePath) is { } path && System.IO.File.Exists(path);

    // Nazwa pliku jak przy pierwszym imporcie: <id atrakcji>-<sha256(tytuł)[:20]>.<rozszerzenie>.
    private static string CachePath(Guid placeId, CommonsImage image)
    {
        var extension = image.Mime switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(image.Title)))[..20];
        return $".local/photo-cache/{placeId}-{digest}{extension}";
    }
}
