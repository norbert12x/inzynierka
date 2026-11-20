namespace Inżynierka.Planning;

// Ustawienia dopełniania (sekcja Planner w appsettings.json).
// SmallMaxVisitMinutes – atrakcja „mała” (mural, ławeczka, pomnik): wizyta do tylu minut.
// OnTheWayMaxDetourMinutes – mała atrakcja trafia do planu tylko po drodze: dokłada najwyżej tyle minut jazdy.
public sealed record FillSettings(int SmallMaxVisitMinutes = 30, int OnTheWayMaxDetourMinutes = 5);

// Dopełnienie PIERWSZEGO planu atrakcjami z katalogu, żeby każdy dzień był wypełniony w swoich godzinach.
// Później (edycja, przeliczenie) planu bez użytkownika nie zmieniamy – dopełnia tylko generowanie.
// 1) Większe atrakcje (wizyta > SmallMaxVisitMinutes): po jednej na dzień na zmianę (dni dzielą się bliskimi
//    kandydatami), za każdym razem ta, która dokłada najmniej jazdy i mieści się bez przekroczenia dnia;
//    atrakcje z nieznanymi godzinami dopiero po tych z godzinami. Dzień kończy, gdy żadna się nie mieści.
// 2) Małe – tylko po drodze między zaplanowanymi punktami (najwyżej OnTheWayMaxDetourMinutes jazdy więcej),
//    nie jako „resztka” na koniec dnia.
// Kandydat musi pasować do zainteresowań (pusta lista – każda kategoria) i nie może być zamknięty ani
// przesuwać innej wizyty poza godziny otwarcia (Insertion). Po dodaniu kolejność dnia jest optymalizowana
// od nowa (to wciąż pierwszy plan), chyba że gorzej wypada z godzinami – wtedy zostaje wstawienie.
public static class TripFiller
{
    public static TripPlan Fill(TripPlan plan, IReadOnlyList<SuggestionCandidate> candidates,
        IReadOnlyCollection<string> interests, ITravelTimes travel, FillSettings? settings = null)
    {
        settings ??= new FillSettings();
        var cached = CachedTravelTimes.Wrap(travel);
        var used = plan.Days.SelectMany(d => d.Visits.Select(v => v.Attraction.PlaceId))
            .Concat(plan.Skipped.Select(a => a.PlaceId))
            .ToHashSet();
        var pool = candidates
            .Where(c => !used.Contains(c.Attraction.PlaceId))
            .Where(c => interests.Count == 0 || c.CategoryCodes.Any(interests.Contains))
            .DistinctBy(c => c.Attraction.PlaceId)
            .Select(c => c.Attraction with { IsMandatory = false, IsAddedByPlanner = true })
            .ToList();

        var days = plan.Days.ToList();
        FillPhase(days, pool, a => a.VisitMinutes > settings.SmallMaxVisitMinutes, maxDetour: null, cached);
        FillPhase(days, pool, a => a.VisitMinutes <= settings.SmallMaxVisitMinutes, settings.OnTheWayMaxDetourMinutes, cached);
        return plan with { Days = days };
    }

    private static void FillPhase(List<DayPlan> days, List<PlanAttraction> pool, Func<PlanAttraction, bool> kind,
        int? maxDetour, ITravelTimes travel)
    {
        var open = Enumerable.Range(0, days.Count).ToList();
        while (open.Count > 0)
        {
            foreach (var index in open.ToList())
            {
                var best = pool.Where(kind)
                    .Select(candidate => (Candidate: candidate, Insertion: Insertion.Best(days[index], candidate, travel)))
                    .Where(o => o.Insertion is not null && (maxDetour is null || o.Insertion.ExtraTravelMinutes <= maxDetour))
                    .OrderBy(o => o.Insertion!.HasUnknownHours)
                    .ThenBy(o => o.Insertion!.ExtraTravelMinutes)
                    .ThenBy(o => o.Candidate.Name, StringComparer.CurrentCulture)
                    .FirstOrDefault();
                if (best.Candidate is null)
                {
                    open.Remove(index);
                    continue;
                }

                pool.Remove(best.Candidate);
                days[index] = Reoptimize(days[index], best.Insertion!, travel);
            }
        }
    }

    private static DayPlan Reoptimize(DayPlan day, InsertionResult insertion, ITravelTimes travel)
    {
        var arranged = DayPlanner.Arrange(day.Settings, insertion.Plan.Visits.Select(v => v.Attraction).ToList(), travel);
        return arranged.OverrunMinutes == 0
               && Insertion.ClosedCount(arranged, except: null) <= Insertion.ClosedCount(insertion.Plan, except: null)
               && arranged.TravelMinutes <= insertion.Plan.TravelMinutes
            ? arranged
            : insertion.Plan;
    }
}
