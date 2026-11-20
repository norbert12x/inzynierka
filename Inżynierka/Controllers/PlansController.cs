using Inżynierka.Configuration;
using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Models;
using Inżynierka.Planning;
using Inżynierka.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace Inżynierka.Controllers;

[ApiController]
[Route("api/plans")]
public class PlansController(CatalogDbContext database, TravelTimeService travelTimes, IOptions<PlannerOptions> options)
    : ControllerBase
{
    // Każda atrakcja ma kategorię (import przypisuje „other”); 30 min – zabezpieczenie, jak w katalogu.
    private const int FallbackVisitMinutes = 30;
    // Górna granica kandydatów na nocleg (obszar MVP ma ok. 150 atrakcji).
    private const int MaxCandidates = 300;

    private PlannerOptions Settings => options.Value;

    // Pierwszy plan wyjazdu bez zapisu – działa dla gościa. Wybrane atrakcje rozłożone na dni, każdy dzień
    // dopełniony atrakcjami z katalogu (fillDays), osobno propozycje do akceptacji. Czasy z macierzy atrakcji
    // w bazie i szacunku; żadna usługa zewnętrzna nie jest wywoływana, współrzędne noclegu trafiają tylko do linków Google Maps.
    [HttpPost("generate")]
    [ProducesResponseType(typeof(PlanGenerateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    // Parametr nullowalny: przy nieczytelnym JSON (zła data, godzina) zostaje sam polski komunikat pola,
    // bez dodatkowego „The request field is required”. Do akcji dochodzi tylko poprawne żądanie ([ApiController]).
    public async Task<ActionResult<PlanGenerateResponse>> Generate(PlanGenerateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
            return ValidationProblem(ModelState);

        var tripDays = BuildDays(request);
        var interests = (request.Interests ?? []).Select(i => i.Trim().ToLowerInvariant()).Distinct().ToList();
        if (interests.Count > 0)
        {
            var known = await database.Categories.AsNoTracking().Select(c => c.Code).ToListAsync(cancellationToken);
            var unknown = interests.Except(known).ToList();
            if (unknown.Count > 0)
                ModelState.AddModelError(nameof(request.Interests),
                    $"Nieznane kategorie: {string.Join(", ", unknown)}. Dostępne kody: GET /api/attraction-categories.");
        }

        var requested = request.Attractions!;
        var ids = requested.Select(a => a.Id!.Value).ToList();
        var places = (await LoadPlaces(database.Places.AsNoTracking().PublicAttractions().Where(p => ids.Contains(p.Id)), cancellationToken))
            .ToDictionary(p => p.Id);
        for (var i = 0; i < requested.Count; i++)
            if (!places.ContainsKey(ids[i]))
                ModelState.AddModelError($"{nameof(request.Attractions)}[{i}].{nameof(PlanAttractionRequest.Id)}",
                    $"Nie ma w katalogu aktywnej atrakcji {ids[i]}.");

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var entrance = Settings.EntranceCategoryCodes;
        var visitSources = new Dictionary<Guid, VisitMinutesSource>();
        var attractions = requested.Select(item =>
        {
            var place = places[item.Id!.Value];
            var visit = item.VisitMinutes is { } minutes
                ? new VisitDuration(minutes, VisitMinutesSource.User)
                : place.Visit;
            visitSources[place.Id] = visit.Source;
            return place.ToPlanAttraction(visit.Minutes, item.Mandatory, entrance);
        }).ToList();

        var withSuggestions = request.IncludeSuggestions && Settings.MaxSuggestionsPerDay > 0;
        var candidates = request.FillDays || withSuggestions
            ? await LoadCandidates(tripDays, ids, interests, entrance, visitSources, cancellationToken)
            : [];

        var travel = await travelTimes.ForPlacesAsync(ids.Concat(candidates.Select(c => c.Attraction.PlaceId)), cancellationToken);
        var trip = TripPlanner.Plan(tripDays.Select(d => d.Day).ToList(), attractions, travel, Settings.OverrunToleranceMinutes);
        // Pierwszy plan wypełnia całe dni; propozycje – na to, co jeszcze zostało.
        if (request.FillDays && candidates.Count > 0)
            trip = TripFiller.Fill(trip, candidates, interests, travel, Settings.Fill);
        var suggestions = withSuggestions && candidates.Count > 0
            ? SuggestionFinder.Find(trip, candidates, interests, travel, Settings.MaxSuggestionsPerDay)
            : [];

        var days = trip.Days.Select((day, i) => DayResponse(day, tripDays[i], i == 0 ? tripDays[0] : tripDays[i - 1], visitSources)).ToList();
        var legs = days.SelectMany(d => d.Visits.Select(v => v.TravelBefore).Append(d.ReturnLeg)).ToList();
        var categories = candidates.ToDictionary(c => c.Attraction.PlaceId, c => c.CategoryCodes);

        return Ok(new PlanGenerateResponse(
            days,
            trip.Skipped.Select(a => new PlanAttractionResponse(a.PlaceId, a.Name, a.IsMandatory, a.VisitMinutes)).ToList(),
            trip.Warnings,
            suggestions.Select(s => new PlanSuggestionResponse(s.Date, s.Attraction.PlaceId, s.Attraction.Name,
                s.Attraction.Location.Latitude, s.Attraction.Location.Longitude, s.Attraction.VisitMinutes,
                categories[s.Attraction.PlaceId], s.InsertAt, s.Arrival, s.Departure, s.ExtraTravelMinutes,
                s.FreeMinutesLeft, s.MatchedCategories, s.Warnings)).ToList(),
            days.Sum(d => d.TravelMinutes),
            legs.Any(l => l.Source == TravelTimeSource.OpenRouteService)
                ? $"{OpenRouteServiceMatrixService.Attribution} ({OpenRouteServiceMatrixService.License})"
                : null));
    }

    // Dzień N startuje z noclegu dnia N−1; brak godzin i przerwy – wartości z Planner.
    // Błędy (koniec przed startem, złe okno posiłku) trafiają do ModelState.
    private List<RequestDay> BuildDays(PlanGenerateRequest request)
    {
        var result = new List<RequestDay>();
        var days = request.Days!;
        var known = new List<PlanLocationResponse> { Location(request.Lodging!) };
        for (var i = 0; i < days.Count; i++)
        {
            var day = days[i];
            var lodging = day.Lodging is null ? known[0] : Location(day.Lodging);
            // Ten sam punkt bez nazwy – nazwa podana wcześniej (np. drugi dzień w tym samym hotelu).
            if (lodging.Name is null && known.FirstOrDefault(k => k.Name is not null
                    && k.Latitude == lodging.Latitude && k.Longitude == lodging.Longitude) is { } named)
                lodging = named;
            known.Add(lodging);
            var start = day.Start ?? Settings.DefaultDayStart;
            var end = day.End ?? Settings.DefaultDayEnd;
            if (end <= start)
                ModelState.AddModelError($"{nameof(request.Days)}[{i}].{nameof(PlanDayRequest.End)}",
                    $"Koniec dnia ({end:HH:mm}) musi być po jego początku ({start:HH:mm}).");

            MealBreakSettings? meal = null;
            if (day.Meal?.Enabled != false)
            {
                var defaults = Settings.DefaultMeal;
                meal = new MealBreakSettings(day.Meal?.Minutes ?? defaults.Minutes,
                    day.Meal?.EarliestStart ?? defaults.EarliestStart, day.Meal?.LatestStart ?? defaults.LatestStart);
                if (meal.LatestStart < meal.EarliestStart)
                    ModelState.AddModelError($"{nameof(request.Days)}[{i}].{nameof(PlanDayRequest.Meal)}",
                        $"Najpóźniejszy start posiłku ({meal.LatestStart:HH:mm}) jest przed najwcześniejszym ({meal.EarliestStart:HH:mm}).");
            }

            var point = new PlanPoint(new GeoPoint(lodging.Latitude, lodging.Longitude));
            result.Add(new RequestDay(new TripDay(day.Date!.Value, point, start, end, meal), lodging));
        }
        return result;
    }

    private static PlanLocationResponse Location(PlanLocationRequest request)
        => new(request.Latitude!.Value, request.Longitude!.Value,
            string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim());

    // Kandydaci do dopełnienia dni i propozycji: aktywne atrakcje spoza planu w promieniu od któregoś noclegu
    // (najbliższe najpierw), pasujące do zainteresowań. Źródło czasu wizyty trafia do visitSources.
    private async Task<List<SuggestionCandidate>> LoadCandidates(IReadOnlyList<RequestDay> days, IReadOnlyList<Guid> planned,
        IReadOnlyList<string> interests, IReadOnlySet<string> entrance, Dictionary<Guid, VisitMinutesSource> visitSources,
        CancellationToken cancellationToken)
    {
        var meters = Settings.CandidateMaxDistanceKm * 1000;
        var found = new Dictionary<Guid, PlaceRow>();
        foreach (var lodging in days.Select(d => d.Lodging).DistinctBy(l => (l.Latitude, l.Longitude)))
        {
            var point = new Point(lodging.Longitude, lodging.Latitude) { SRID = 4326 };
            var query = database.Places.AsNoTracking().PublicAttractions()
                .Where(p => !planned.Contains(p.Id) && EF.Functions.IsWithinDistance(p.Location, point, meters, true));
            if (interests.Count > 0)
                query = query.Where(p => p.Categories.Any(c => interests.Contains(c.CategoryCode)));
            var nearest = query.OrderBy(p => EF.Functions.Distance(p.Location, point, true)).ThenBy(p => p.Id)
                .Take(MaxCandidates);
            foreach (var row in await LoadPlaces(nearest, cancellationToken))
                found.TryAdd(row.Id, row);
        }

        foreach (var place in found.Values)
            visitSources.TryAdd(place.Id, place.Visit.Source);
        return found.Values
            .Select(p => new SuggestionCandidate(p.ToPlanAttraction(p.Visit.Minutes, mandatory: false, entrance),
                p.Categories.Select(c => c.Code).ToList()))
            .ToList();
    }

    private static async Task<List<PlaceRow>> LoadPlaces(IQueryable<CatalogPlace> query, CancellationToken cancellationToken)
        => await query
            .Select(p => new PlaceRow(p.Id, p.Name, p.Location, p.OpeningHours, p.EstimatedVisitMinutes,
                p.Categories.OrderBy(c => c.CategoryCode)
                    .Select(c => new CategoryRow(c.CategoryCode, c.Category.DefaultVisitMinutes)).ToList()))
            .ToListAsync(cancellationToken);

    private static PlanDayResponse DayResponse(DayPlan day, RequestDay current, RequestDay previous,
        IReadOnlyDictionary<Guid, VisitMinutesSource> visitSources)
    {
        var start = previous.Lodging;
        var from = new GeoPoint(start.Latitude, start.Longitude);
        var visits = new List<PlanVisitResponse>(day.Visits.Count);
        foreach (var visit in day.Visits)
        {
            var attraction = visit.Attraction;
            visits.Add(new PlanVisitResponse(attraction.PlaceId, attraction.Name, attraction.Location.Latitude,
                attraction.Location.Longitude, attraction.IsMandatory, attraction.IsAddedByPlanner, attraction.VisitMinutes,
                visitSources[attraction.PlaceId],
                Leg(visit.TravelBefore, from, attraction.Location), visit.Arrival, visit.WaitMinutes, visit.Departure,
                attraction.OpeningHours));
            from = attraction.Location;
        }

        var end = current.Lodging;
        var endPoint = new GeoPoint(end.Latitude, end.Longitude);
        return new PlanDayResponse(day.Settings.Date, start, end, day.Settings.ActiveFrom, day.Settings.ActiveTo,
            visits, day.Meal, Leg(day.ReturnLeg, from, endPoint), day.ReturnArrival, day.OverrunMinutes, day.TravelMinutes,
            day.Warnings,
            GoogleMapsLinks.Day(new GeoPoint(start.Latitude, start.Longitude),
                day.Visits.Select(v => v.Attraction.Location).ToList(), endPoint));
    }

    private static PlanLegResponse Leg(TravelLeg leg, GeoPoint from, GeoPoint to)
        => new(leg.Mode, leg.Minutes, leg.DistanceMeters, leg.Source, leg.IsApproximate,
            GoogleMapsLinks.Leg(from, to, leg.Mode), GoogleMapsLinks.Transit(from, to));

    private sealed record RequestDay(TripDay Day, PlanLocationResponse Lodging);

    private sealed record CategoryRow(string Code, int DefaultVisitMinutes);

    private sealed record PlaceRow(Guid Id, string Name, Point Location, string? OpeningHours, int? EstimatedVisitMinutes,
        List<CategoryRow> Categories)
    {
        public VisitDuration Visit => VisitDuration.Resolve(EstimatedVisitMinutes,
            Categories.Select(c => c.DefaultVisitMinutes), FallbackVisitMinutes);

        public PlanAttraction ToPlanAttraction(int visitMinutes, bool mandatory, IReadOnlySet<string> entrance)
            => new(Id, Name, new GeoPoint(Location.Y, Location.X), visitMinutes, mandatory, OpeningHours,
                EntranceCategories.HasEntrance(Categories.Select(c => c.Code), entrance));
    }
}
