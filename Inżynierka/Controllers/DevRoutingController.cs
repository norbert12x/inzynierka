using System.Net;
using System.Text.Json;
using Inżynierka.Configuration;
using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Infrastructure;
using Inżynierka.Models;
using Inżynierka.Planning;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inżynierka.Controllers;

// Czasy przejazdów (E3), tylko Development: diagnostyka szacunku i pobieranie macierzy atrakcji z ORS.
[ApiController]
[DevelopmentOnly]
[Route("api/dev/routing")]
public sealed class DevRoutingController(
    TravelTimeEstimator estimator,
    CatalogDbContext database,
    OpenRouteServiceMatrixService openRouteService,
    IOptions<RoutingOptions> options,
    ILogger<DevRoutingController> logger) : ControllerBase
{
    // Plan Standard: 40 zapytań macierzy na minutę – co 2 s to najwyżej 30.
    private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(2);

    // Szacunek dla dwóch punktów – do ręcznego porównania z trasą w Google Maps. Nie wysyła zapytań na zewnątrz.
    [HttpGet("estimate")]
    [ProducesResponseType(typeof(TravelEstimateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<TravelEstimateResponse> Estimate([FromQuery] TravelEstimateQuery query)
    {
        // [ApiController] zwraca 400 przed wejściem tutaj, gdy brakuje parametru.
        var from = new GeoPoint(query.FromLatitude!.Value, query.FromLongitude!.Value);
        var to = new GeoPoint(query.ToLatitude!.Value, query.ToLongitude!.Value);
        var meters = TravelTimeEstimator.StraightLineMeters(from, to);

        return Ok(new TravelEstimateResponse(
            (int)Math.Round(meters),
            estimator.EstimateForDistance(meters),
            estimator.EstimateForDistance(meters, TravelMode.Walking),
            estimator.EstimateForDistance(meters, TravelMode.Driving),
            estimator.Settings));
    }

    // Pobiera z ORS brakujące pary aktywnych atrakcji (w progu odległości) i zapisuje w routing.travel_times.
    // Zapis po każdym zapytaniu: po błędzie lub limicie ponowne wywołanie pobierze tylko to, czego brakuje.
    [HttpPost("matrix")]
    [ProducesResponseType(typeof(TravelMatrixResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<TravelMatrixResponse>> Matrix(CancellationToken cancellationToken,
        [FromQuery] string profile = RoutingProfiles.DrivingCar,
        // true = tylko policz brakujące pary i potrzebne zapytania, bez wysyłania do ORS.
        [FromQuery] bool dryRun = true,
        [FromQuery] int maxRequests = 10)
    {
        if (!RoutingProfiles.All.Contains(profile))
            ModelState.AddModelError(nameof(profile), "Dozwolone: driving-car, foot-walking.");
        if (maxRequests is < 1 or > 100)
            ModelState.AddModelError(nameof(maxRequests), "Od 1 do 100 zapytań na wywołanie (limit dzienny ORS: 500).");
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);
        if (!dryRun && !openRouteService.IsConfigured)
            return Problem(statusCode: 503, title: "Brak klucza OpenRouteService.",
                detail: "Zapisz klucz w .local/ors-key.txt i uruchom aplikację ponownie.");

        var places = (await database.Places.AsNoTracking().PublicAttractions()
                .Select(p => new { p.Id, p.Location })
                .ToListAsync(cancellationToken))
            .Select(p => new MatrixPlace(p.Id, new GeoPoint(p.Location.Y, p.Location.X)))
            .ToList();
        var known = (await database.TravelTimes.AsNoTracking()
                .Where(t => t.Profile == profile)
                .Select(t => new { t.FromPlaceId, t.ToPlaceId })
                .ToListAsync(cancellationToken))
            .Select(t => (t.FromPlaceId, t.ToPlaceId))
            .ToHashSet();

        var maxDistanceKm = profile == RoutingProfiles.FootWalking
            ? options.Value.Matrix.WalkingMaxDistanceKm
            : options.Value.Matrix.DrivingMaxDistanceKm;
        var batches = TravelMatrixBatches.Plan(places, (from, to) => known.Contains((from, to)), maxDistanceKm * 1000);
        var missingPairs = batches.Sum(b => b.Pairs.Count);

        int sent = 0, saved = 0, unreachable = 0;
        if (!dryRun)
        {
            foreach (var batch in batches.Take(maxRequests))
            {
                if (sent > 0)
                    await Task.Delay(RequestInterval, cancellationToken);

                MatrixResult result;
                try
                {
                    result = await openRouteService.GetMatrixAsync(profile,
                        batch.Sources.Select(p => p.Location).ToList(),
                        batch.Destinations.Select(p => p.Location).ToList(),
                        cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Problem(statusCode: 504, title: "Przekroczono czas odpowiedzi OpenRouteService.", detail: Progress(saved));
                }
                catch (HttpRequestException exception)
                {
                    logger.LogWarning("Macierz ORS nie powiodła się: {Message}", exception.Message);
                    return Problem(statusCode: 503, title: ProviderErrorTitle(exception.StatusCode),
                        detail: $"{exception.Message} {Progress(saved)}");
                }
                catch (JsonException)
                {
                    logger.LogWarning("OpenRouteService zwrócił nieprawidłową macierz.");
                    return Problem(statusCode: 502, title: "OpenRouteService zwrócił nieprawidłowe dane.", detail: Progress(saved));
                }
                sent++;

                var fetchedAt = DateTimeOffset.UtcNow;
                foreach (var (source, destination) in batch.Pairs)
                {
                    var duration = result.Durations[source][destination];
                    var distance = result.Distances[source][destination];
                    if (duration is null)
                        unreachable++;
                    database.TravelTimes.Add(new TravelTime
                    {
                        FromPlaceId = batch.Sources[source].Id,
                        ToPlaceId = batch.Destinations[destination].Id,
                        Profile = profile,
                        DurationSeconds = duration is null ? null : (int)Math.Round(duration.Value),
                        DistanceMeters = duration is null || distance is null ? null : (int)Math.Round(distance.Value),
                        Provider = TravelTimeProviders.OpenRouteService,
                        FetchedAt = fetchedAt
                    });
                }
                await database.SaveChangesAsync(cancellationToken);
                database.ChangeTracker.Clear();
                saved += batch.Pairs.Count;
            }
        }

        return Ok(new TravelMatrixResponse(profile, dryRun, places.Count, maxDistanceKm, missingPairs, batches.Count,
            sent, saved, unreachable, batches.Count - sent,
            OpenRouteServiceMatrixService.Attribution, OpenRouteServiceMatrixService.License));
    }

    // Porównuje szacunek z macierzą ORS z bazy i proponuje nowe parametry Routing:Estimate.
    // Tylko odczyt – nie zmienia konfiguracji ani bazy, nie wysyła zapytań do ORS.
    [HttpGet("calibration")]
    [ProducesResponseType(typeof(CalibrationReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<CalibrationReport>> Calibration(CancellationToken cancellationToken)
    {
        // Współrzędne osobno i w pamięci: Location to geography, a Point.X/Y w zapytaniu EF tłumaczy na
        // ST_X/ST_Y, które w PostGIS działają tylko dla geometry.
        var locations = (await database.Places.AsNoTracking()
                .Select(p => new { p.Id, p.Location })
                .ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => new GeoPoint(p.Location.Y, p.Location.X));
        var rows = await database.TravelTimes.AsNoTracking()
            .Where(t => t.DurationSeconds != null && t.DistanceMeters != null)
            .Select(t => new { t.FromPlaceId, t.ToPlaceId, t.Profile, t.DurationSeconds, t.DistanceMeters })
            .ToListAsync(cancellationToken);

        var samples = rows
            .Where(r => locations.ContainsKey(r.FromPlaceId) && locations.ContainsKey(r.ToPlaceId))
            .Select(r => new CalibrationSample(
                r.Profile == RoutingProfiles.FootWalking ? TravelMode.Walking : TravelMode.Driving,
                TravelTimeEstimator.StraightLineMeters(locations[r.FromPlaceId], locations[r.ToPlaceId]),
                r.DistanceMeters!.Value,
                r.DurationSeconds!.Value))
            .ToList();

        return Ok(TravelCalibration.Calibrate(samples, options.Value.Estimate));
    }

    private static string Progress(int saved) => saved == 0
        ? "Nic nie zapisano."
        : $"Zapisano {saved} par przed błędem – ponowne wywołanie pobierze tylko brakujące.";

    private static string ProviderErrorTitle(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.TooManyRequests => "Wyczerpany limit OpenRouteService (500 macierzy dziennie, 40 na minutę).",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "OpenRouteService odrzucił klucz API.",
        HttpStatusCode.BadRequest => "OpenRouteService odrzucił zapytanie.",
        _ => "OpenRouteService jest chwilowo niedostępny."
    };
}
