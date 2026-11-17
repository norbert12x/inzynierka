using System.Security.Cryptography;
using System.Text;
using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Models;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Controllers;

// Narzędzia tylko dla Development (poza nim 404) do pracy nad zdjęciami katalogu:
// - repair: uzupełnia brakujące pliki w .local/photo-cache (bez zmian w bazie),
// - photo-candidates: szuka kilku propozycji zdjęć jednej atrakcji w Wikimedia Commons (zapis jako pending_review),
// - review: zatwierdza lub odrzuca zdjęcie.
// Docelowo przegląd zdjęć przejdzie do panelu administratora (E6.6).
[ApiController]
[Route("api/dev/photos")]
public sealed class DevPhotosController(
    IWebHostEnvironment environment,
    PhotoDownloadService downloader,
    WikimediaCommonsService commons,
    ILogger<DevPhotosController> logger) : ControllerBase
{
    [HttpPost("~/api/dev/attractions/{attractionId:guid}/photo-candidates")]
    [ProducesResponseType(typeof(PhotoCandidatesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PhotoCandidatesResponse>> Candidates(
        Guid attractionId,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken,
        // Ile nowych propozycji zapisać (1–20).
        [FromQuery] int limit = 5,
        // Promień wyszukiwania zdjęć z geotagiem; 0 = tylko po nazwie. Większy promień = więcej sąsiednich budynków.
        [FromQuery] int radiusMeters = 50,
        // "place" = zdjęcia atrakcji, "exhibit" = eksponaty/wnętrza (podkategorie kolekcji w Commons).
        [FromQuery] string kind = PhotoKinds.Place)
    {
        if (!environment.IsDevelopment())
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono.");
        if (limit is < 1 or > 20)
            ModelState.AddModelError(nameof(limit), "Limit od 1 do 20.");
        if (radiusMeters is < 0 or > 500)
            ModelState.AddModelError(nameof(radiusMeters), "Promień od 0 do 500 m.");
        if (kind is not (PhotoKinds.Place or PhotoKinds.Exhibit))
            ModelState.AddModelError(nameof(kind), "Dozwolone: place, exhibit.");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Brak konfiguracji bazy.");

        try
        {
            var place = await database.Places.AsNoTracking()
                .Where(p => p.Id == attractionId && p.Kind == "attraction" && p.Status != "removed")
                .Select(p => new
                {
                    p.Id, p.Name, p.City, p.Location,
                    Known = p.Photos.Select(ph => ph.SourceFile).ToList(),
                    RawData = p.Sources.Select(s => s.RawData).ToList()
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (place is null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono atrakcji.");

            var methods = new Dictionary<string, string>();
            void Collect(IEnumerable<string> titles, string method)
            {
                foreach (var title in titles)
                    methods.TryAdd(title, method);
            }

            // Kategoria Commons samego obiektu (Wikidata P373); kategorie osób/tematów (subject:wikidata) pomijamy.
            var wikidataId = place.RawData.Select(WikimediaCommonsService.ObjectWikidataId).FirstOrDefault(id => id is not null);
            var category = wikidataId is null ? null : await commons.GetCommonsCategoryAsync(wikidataId, cancellationToken);
            IReadOnlyList<string> subcategories = category is null
                ? []
                : await commons.GetCategoryMembersAsync(category, "subcat", 50, cancellationToken);
            string? note = null;

            if (kind == PhotoKinds.Exhibit)
            {
                // Eksponaty: tylko podkategorie kolekcji/wnętrz – bez szukania po nazwie i w promieniu.
                var exhibitCategories = subcategories.Where(WikimediaCommonsService.IsExhibitCategory).Take(5).ToList();
                foreach (var sub in exhibitCategories)
                    Collect(await commons.GetCategoryMembersAsync(sub, "file", 50, cancellationToken), "commons_exhibits:" + sub);
                if (category is null)
                    note = "Atrakcja nie ma kategorii w Wikimedia Commons (brak powiązania Wikidata) – eksponatów nie da się wyszukać.";
                else if (exhibitCategories.Count == 0)
                    note = $"Kategoria „{category}” nie ma podkategorii kolekcji ani wnętrz.";
            }
            else
            {
                // Zdjęcia miejsca: najpierw kategoria obiektu (najtrafniejsza), potem po nazwie, na końcu w promieniu.
                if (category is not null)
                {
                    Collect(await commons.GetCategoryMembersAsync(category, "file", 50, cancellationToken), "commons_category:" + category);
                    foreach (var sub in subcategories.Where(s => !WikimediaCommonsService.IsExhibitCategory(s)).Take(3))
                        Collect(await commons.GetCategoryMembersAsync(sub, "file", 20, cancellationToken), "commons_category:" + sub);
                }

                var city = place.City ?? "";
                var byName = methods.Count;
                Collect(await commons.SearchFilesAsync($"\"{place.Name}\" {city}", 20, cancellationToken), "commons_name_search");
                if (methods.Count == byName)
                    Collect(await commons.SearchFilesAsync($"{place.Name} {city}", 20, cancellationToken), "commons_name_search");
                if (radiusMeters > 0)
                    Collect(await commons.GeoSearchFilesAsync(place.Location.Y, place.Location.X, radiusMeters, 20, cancellationToken),
                        $"commons_geosearch_{radiusMeters}m");
            }

            var known = place.Known.ToHashSet();
            var newTitles = methods.Keys.Where(t => !known.Contains(t)).ToList();
            var images = (await commons.GetImagesAsync(newTitles, cancellationToken))
                .OrderBy(i => newTitles.IndexOf(i.Title)).ToList();

            var added = new List<PhotoCandidate>();
            var skipped = new List<PhotoCandidateSkip>();
            foreach (var image in images)
            {
                if (added.Count >= limit)
                    break;
                var reason = WikimediaCommonsService.RejectionReason(image);
                if (reason is not null)
                {
                    skipped.Add(Skip(image, reason));
                    continue;
                }

                // Nazwa pliku jak w scripts/fetch_place_photos.py: <id atrakcji>-<sha256(tytuł)[:20]>.<rozszerzenie>.
                var extension = image.Mime switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
                var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(image.Title)))[..20];
                var relative = $".local/photo-cache/{place.Id}-{digest}{extension}";
                var path = PhotoCache.ResolveFile(environment.ContentRootPath, relative)!;

                await Task.Delay(400, cancellationToken);
                var error = System.IO.File.Exists(path)
                    ? null
                    : await downloader.DownloadAsync(image.ThumbnailUrl, path, image.Mime, cancellationToken);
                if (error is not null)
                {
                    skipped.Add(Skip(image, error));
                    continue;
                }

                var photo = new PlacePhoto
                {
                    Id = Guid.CreateVersion7(),
                    PlaceId = place.Id,
                    Provider = "wikimedia_commons",
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
                    Status = "pending_review",
                    Kind = kind,
                    Caption = string.IsNullOrWhiteSpace(image.Caption) ? null : image.Caption
                };
                database.Photos.Add(photo);
                await database.SaveChangesAsync(cancellationToken);
                added.Add(new PhotoCandidate(photo.Id,
                    $"/api/attractions/{place.Id}/photos/{photo.Id}/image?includePending=true",
                    image.Title, image.Author, image.License, image.DescriptionUrl, photo.MatchMethod, photo.Kind, photo.Caption));
            }

            if (note is null && methods.Count == 0)
                note = "Nic nie znaleziono w Wikimedia Commons.";
            logger.LogInformation("Propozycje zdjęć ({Kind}) dla {PlaceId}: dodano {Added}, pominięto {Skipped}.",
                kind, place.Id, added.Count, skipped.Count);
            return Ok(new PhotoCandidatesResponse(place.Id, place.Name, kind, category, note, added, skipped,
                methods.Count - newTitles.Count));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning("Wikimedia Commons niedostępne (HTTP={StatusCode}).", (int?)exception.StatusCode);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Wikimedia Commons jest chwilowo niedostępne.");
        }
        catch (System.Text.Json.JsonException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: "Wikimedia Commons zwróciło nieprawidłową odpowiedź.");
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException or DbUpdateException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Zapis propozycji zdjęć nie powiódł się ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Nie udało się zapisać propozycji w bazie.");
        }
    }

    private static PhotoCandidateSkip Skip(CommonsImage image, string reason)
        => new(image.Title, reason, image.ThumbnailUrl, image.DescriptionUrl, image.License, image.Author);

    // Dane dla strony przeglądu /dev/zdjecia (DevTools/zdjecia.html).
    [HttpGet("queue")]
    [ProducesResponseType(typeof(PhotoReviewQueue), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PhotoReviewQueue>> Queue(
        [FromServices] IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono.");
        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Brak konfiguracji bazy.");

        try
        {
            var reviewable = database.Places.AsNoTracking()
                .Where(p => p.Kind == "attraction" && (p.Status == "active" || p.Status == "awaiting_photo"));

            var pendingRows = await database.Photos.AsNoTracking()
                .Where(ph => ph.Status == "pending_review" && ph.Place.Kind == "attraction"
                    && (ph.Place.Status == "active" || ph.Place.Status == "awaiting_photo"))
                .OrderBy(ph => ph.Place.Name).ThenBy(ph => ph.PlaceId).ThenBy(ph => ph.FetchedAtUtc)
                .Select(ph => new
                {
                    ph.Id, ph.PlaceId, PlaceName = ph.Place.Name, ph.Place.City, PlaceStatus = ph.Place.Status,
                    ph.Place.Location, ph.Author, ph.License, ph.LicenseUrl, ph.SourcePageUrl, ph.SourceFile,
                    ph.MatchMethod, ph.CachedRelativePath, ph.Kind, ph.Caption,
                    // Liczymy zatwierdzone tego samego rodzaju (zdjęcia miejsca albo eksponaty).
                    ApprovedCount = ph.Place.Photos.Count(o => o.Status == "approved" && o.Kind == ph.Kind),
                    IsMuseum = ph.Place.Categories.Any(c => c.CategoryCode == "museums")
                })
                .ToListAsync(cancellationToken);

            var pending = pendingRows.Select(r =>
            {
                var path = PhotoCache.ResolveFile(environment.ContentRootPath, r.CachedRelativePath);
                return new PendingPhoto(r.Id, r.PlaceId, r.PlaceName, r.City, r.PlaceStatus,
                    r.Location.Y, r.Location.X,
                    $"/api/attractions/{r.PlaceId}/photos/{r.Id}/image?includePending=true",
                    path is not null && System.IO.File.Exists(path),
                    r.Author, r.License, r.LicenseUrl, r.SourcePageUrl, r.SourceFile, r.MatchMethod, r.ApprovedCount,
                    r.Kind, r.Caption, r.IsMuseum);
            }).ToList();

            var places = await reviewable
                .OrderBy(p => p.Name)
                .Select(p => new PlaceForReview(p.Id, p.Name, p.City, p.Status,
                    p.Categories.Any(c => c.CategoryCode == "museums"),
                    p.Photos.Count(ph => ph.Status == "approved" && ph.Kind == PhotoKinds.Place),
                    p.Photos.Count(ph => ph.Status == "approved" && ph.Kind == PhotoKinds.Exhibit),
                    p.Photos.Count(ph => ph.Status == "pending_review")))
                .ToListAsync(cancellationToken);

            return Ok(new PhotoReviewQueue(pending, places));
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Odczyt kolejki zdjęć nie powiódł się ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Nie udało się odczytać zdjęć z bazy.");
        }
    }

    [HttpPost("{photoId:guid}/review")]
    [ProducesResponseType(typeof(PhotoReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PhotoReviewResponse>> Review(
        Guid photoId,
        [FromBody] PhotoReviewRequest request,
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono.");
        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Brak konfiguracji bazy.");

        try
        {
            var photo = await database.Photos.Include(p => p.Place)
                .SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
            if (photo is null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono zdjęcia.");

            var kindChanged = request.Kind is not null && request.Kind != photo.Kind;
            var becomesApproved = request.Status == "approved" && photo.Status != "approved";
            if (request.Kind is not null)
                photo.Kind = request.Kind;
            if (request.Status is not null)
                photo.Status = request.Status;

            // Nowo zatwierdzone (albo przeniesione do innej galerii) trafia na koniec – nie zmienia zdjęcia głównego.
            if (photo.Status == "approved" && (becomesApproved || kindChanged))
            {
                var last = await database.Photos
                    .Where(p => p.PlaceId == photo.PlaceId && p.Kind == photo.Kind && p.Status == "approved" && p.Id != photo.Id)
                    .MaxAsync(p => (int?)p.SortOrder, cancellationToken);
                photo.SortOrder = last is null ? 0 : last.Value + 1;
            }

            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Zdjęcie {PhotoId}: status {Status}, rodzaj {Kind}.", photo.Id, photo.Status, photo.Kind);
            return Ok(new PhotoReviewResponse(photo.Id, photo.PlaceId, photo.Place.Name, photo.Status, photo.Kind, photo.SortOrder));
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException or DbUpdateException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Zmiana statusu zdjęcia nie powiodła się ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Nie udało się zapisać decyzji w bazie.");
        }
    }

    // Ustawia zatwierdzone zdjęcie jako pierwsze w swojej galerii (główne zdjęcie atrakcji / pierwszy eksponat).
    [HttpPost("{photoId:guid}/primary")]
    [ProducesResponseType(typeof(PhotoReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PhotoReviewResponse>> MakePrimary(
        Guid photoId, [FromServices] IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono.");
        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Brak konfiguracji bazy.");

        try
        {
            var photo = await database.Photos.Include(p => p.Place).SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
            if (photo is null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono zdjęcia.");
            if (photo.Status != "approved")
                return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Najpierw zatwierdź zdjęcie.");

            // Nowa kolejność: wybrane = 0, pozostałe zatwierdzone tego rodzaju 1, 2, … w dotychczasowej kolejności.
            var gallery = await database.Photos
                .Where(p => p.PlaceId == photo.PlaceId && p.Kind == photo.Kind && p.Status == "approved" && p.Id != photo.Id)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.FetchedAtUtc).ThenBy(p => p.Id)
                .ToListAsync(cancellationToken);
            photo.SortOrder = 0;
            for (var i = 0; i < gallery.Count; i++)
                gallery[i].SortOrder = i + 1;

            await database.SaveChangesAsync(cancellationToken);
            return Ok(new PhotoReviewResponse(photo.Id, photo.PlaceId, photo.Place.Name, photo.Status, photo.Kind, photo.SortOrder));
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException or DbUpdateException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Zmiana kolejności zdjęć nie powiodła się ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Nie udało się zapisać kolejności w bazie.");
        }
    }

    // Wszystkie zdjęcia jednej atrakcji, niezależnie od statusu – do ponownego przeglądu.
    [HttpGet("~/api/dev/attractions/{attractionId:guid}/photos")]
    [ProducesResponseType(typeof(AttractionPhotoReview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AttractionPhotoReview>> AttractionPhotos(
        Guid attractionId, [FromServices] IServiceProvider services, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono.");
        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Brak konfiguracji bazy.");

        try
        {
            var place = await database.Places.AsNoTracking().AsSplitQuery()
                .Where(p => p.Id == attractionId && p.Kind == "attraction")
                .Select(p => new
                {
                    p.Id, p.Name, p.City, p.Status, p.Location,
                    IsMuseum = p.Categories.Any(c => c.CategoryCode == "museums"),
                    RawData = p.Sources.Select(s => s.RawData).ToList(),
                    Photos = p.Photos
                        .OrderBy(ph => ph.Status == "approved" ? 0 : ph.Status == "pending_review" ? 1 : 2)
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

            var primary = new HashSet<Guid>(place.Photos.Where(ph => ph.Status == "approved")
                .GroupBy(ph => ph.Kind).Select(g => g.First().Id));
            var photos = place.Photos.Select(ph =>
            {
                var path = PhotoCache.ResolveFile(environment.ContentRootPath, ph.CachedRelativePath);
                return new ReviewPhoto(ph.Id,
                    $"/api/attractions/{place.Id}/photos/{ph.Id}/image?includePending=true",
                    path is not null && System.IO.File.Exists(path), ph.Status, ph.Kind, ph.Caption, ph.SortOrder,
                    primary.Contains(ph.Id), ph.Provider, ph.Author, ph.License, ph.LicenseUrl, ph.SourcePageUrl,
                    ph.SourceFile, ph.MatchMethod);
            }).ToList();

            var wikidata = place.RawData.Select(WikimediaCommonsService.ObjectWikidataId).FirstOrDefault(id => id is not null);
            return Ok(new AttractionPhotoReview(place.Id, place.Name, place.City, place.Status, place.IsMuseum,
                place.Location.Y, place.Location.X, wikidata, photos));
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Odczyt zdjęć atrakcji nie powiódł się ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Nie udało się odczytać zdjęć z bazy.");
        }
    }

    [HttpPost("repair")]
    [ProducesResponseType(typeof(PhotoRepairResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PhotoRepairResponse>> Repair(
        [FromServices] IServiceProvider services,
        CancellationToken cancellationToken,
        // true = tylko raport braków, bez pobierania.
        [FromQuery] bool dryRun = true,
        [FromQuery] int limit = 50)
    {
        if (!environment.IsDevelopment())
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Nie znaleziono.");
        if (limit is < 1 or > 200)
        {
            ModelState.AddModelError(nameof(limit), "Limit od 1 do 200.");
            return ValidationProblem(ModelState);
        }

        var database = services.GetService<CatalogDbContext>();
        if (database is null)
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Brak konfiguracji bazy.");

        List<PhotoRow> rows;
        try
        {
            rows = await database.Photos.AsNoTracking()
                .Where(p => p.Status != "rejected")
                .OrderBy(p => p.Place.Name).ThenBy(p => p.Id)
                .Select(p => new PhotoRow(p.Id, p.PlaceId, p.Place.Name, p.Provider, p.Status,
                    p.ThumbnailUrl, p.CachedRelativePath))
                .ToListAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or TimeoutException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Odczyt zdjęć do naprawy nie powiódł się ({ExceptionType}).", exception.GetType().Name);
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Nie udało się odczytać zdjęć z bazy.");
        }

        var items = new List<PhotoRepairItem>();
        int present = 0, missing = 0, downloaded = 0, failed = 0, attempts = 0;
        foreach (var row in rows)
        {
            var path = PhotoCache.ResolveFile(environment.ContentRootPath, row.CachedRelativePath);
            var contentType = path is null ? null : PhotoCache.ContentType(path);
            if (path is null || contentType is null)
            {
                items.Add(Item(row, "invalid_path", "Ścieżka pliku poza .local/photo-cache albo nieobsługiwany format."));
                continue;
            }
            if (System.IO.File.Exists(path))
            {
                present++;
                continue;
            }

            missing++;
            if (row.Provider != "wikimedia_commons")
            {
                items.Add(Item(row, "manual_required", "Zdjęcie własne – skopiuj plik ręcznie (scripts/import_place_photo.py)."));
                continue;
            }
            if (dryRun || attempts >= limit)
            {
                items.Add(Item(row, "missing", null));
                continue;
            }

            attempts++;
            // Odstęp między zapytaniami, jak w skrypcie Python – nie obciążamy Wikimedia.
            await Task.Delay(400, cancellationToken);
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
            {
                downloaded++;
                items.Add(Item(row, "downloaded", null));
            }
            else
            {
                failed++;
                items.Add(Item(row, "failed", error));
            }
        }

        logger.LogInformation("Naprawa zdjęć: brakowało {Missing}, pobrano {Downloaded}, błędy {Failed}.", missing, downloaded, failed);
        var remaining = items.Count(i => i.Outcome == "missing");
        return Ok(new PhotoRepairResponse(dryRun, rows.Count, present, missing, downloaded, failed, remaining, items));
    }

    private static PhotoRepairItem Item(PhotoRow row, string outcome, string? detail)
        => new(row.Id, row.PlaceId, row.PlaceName, row.Status, outcome, detail);

    private sealed record PhotoRow(Guid Id, Guid PlaceId, string PlaceName, string Provider, string Status,
        string ThumbnailUrl, string CachedRelativePath);
}
