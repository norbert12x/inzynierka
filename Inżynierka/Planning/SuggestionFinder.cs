namespace Inżynierka.Planning;

// Propozycje dodatkowych atrakcji na wolny czas w dniach planu – zwracane osobno, do akceptacji
// (plan się nie zmienia). Kandydat musi pasować do zainteresowań (pusta lista – każda kategoria),
// mieścić się w godzinach dnia bez przekroczenia i bez zmiany kolejności zaplanowanych wizyt
// (wstawiamy go w najlepsze miejsce trasy). Odrzucamy miejsca, w których kandydat byłby zamknięty
// albo przez które inna wizyta wypadłaby poza godzinami otwarcia. Brak godzin nie wyklucza – propozycja
// niesie wtedy ostrzeżenie. Kolejność: najmniej dodatkowej jazdy, potem więcej pasujących zainteresowań.
// Każdy kandydat trafia najwyżej do jednego dnia – tego, w którym dokłada najmniej jazdy.
public static class SuggestionFinder
{
    public const int DefaultMaxPerDay = 3;

    public static IReadOnlyList<PlanSuggestion> Find(TripPlan plan, IReadOnlyList<SuggestionCandidate> candidates,
        IReadOnlyCollection<string> interests, ITravelTimes travel, int maxPerDay = DefaultMaxPerDay)
    {
        if (maxPerDay <= 0)
            return [];

        var cached = CachedTravelTimes.Wrap(travel);
        var planned = plan.Days.SelectMany(d => d.Visits.Select(v => v.Attraction.PlaceId))
            .Concat(plan.Skipped.Select(a => a.PlaceId))
            .ToHashSet();

        var best = new List<PlanSuggestion>();
        foreach (var candidate in candidates.DistinctBy(c => c.Attraction.PlaceId))
        {
            if (planned.Contains(candidate.Attraction.PlaceId))
                continue;
            IReadOnlyList<string> matched = interests.Count == 0 ? [] : candidate.CategoryCodes.Where(interests.Contains).ToList();
            if (interests.Count > 0 && matched.Count == 0)
                continue;

            var suggestion = plan.Days
                .Select(day => BestInsertion(day, candidate.Attraction, matched, cached))
                .OfType<PlanSuggestion>()
                .OrderBy(s => s.ExtraTravelMinutes)
                .ThenBy(s => s.Date)
                .FirstOrDefault();
            if (suggestion is not null)
                best.Add(suggestion);
        }

        return best
            .GroupBy(s => s.Date)
            .OrderBy(g => g.Key)
            .SelectMany(g => g
                .OrderBy(s => s.ExtraTravelMinutes)
                .ThenByDescending(s => s.MatchedCategories.Count)
                .ThenBy(s => s.Attraction.Name, StringComparer.CurrentCulture)
                .Take(maxPerDay))
            .ToList();
    }

    // Najlepsza pozycja w dniu (Insertion.Best) albo null, gdy nigdzie się nie mieści.
    private static PlanSuggestion? BestInsertion(DayPlan day, PlanAttraction candidate, IReadOnlyList<string> matched,
        ITravelTimes travel)
    {
        if (Insertion.Best(day, candidate, travel) is not { } insertion)
            return null;

        var visit = insertion.Plan.Visits[insertion.Position];
        return new PlanSuggestion(day.Settings.Date, candidate, insertion.Position, visit.Arrival, visit.Departure,
            insertion.ExtraTravelMinutes,
            DayScheduler.ToMinutes(day.Settings.ActiveTo) - DayScheduler.ToMinutes(insertion.Plan.ReturnArrival),
            matched, insertion.OwnWarnings);
    }
}
