namespace Inżynierka.Planning;

// Automatyczny plan jednego dnia: kolejność (RouteOptimizer) + godziny (DayScheduler).
// Przekroczenie godzin dnia o najwyżej overrunToleranceMinutes – nic nie usuwamy, tylko ostrzegamy
// (użytkownik sam usuwa wybraną atrakcję albo zostawia całość). Większe przekroczenie – usuwamy
// najkrótsze opcjonalne, aż mieści się w tolerancji. Obowiązkowe zostają zawsze (z ostrzeżeniem).
public static class DayPlanner
{
    public const int DefaultOverrunToleranceMinutes = 30;

    public static DayPlan Plan(DaySettings day, IReadOnlyList<PlanAttraction> attractions, ITravelTimes travel,
        int overrunToleranceMinutes = DefaultOverrunToleranceMinutes)
    {
        Validate(day, attractions, overrunToleranceMinutes);
        var cached = CachedTravelTimes.Wrap(travel);
        var selected = attractions.ToList();
        var skipped = new List<PlanAttraction>();

        var plan = Build(day, selected, cached);
        while (plan.OverrunMinutes > overrunToleranceMinutes && selected.Any(a => !a.IsMandatory))
        {
            // Najkrótsza wizyta; remis – wcześniejsza w trasie.
            var drop = plan.Visits.Select(v => v.Attraction).Where(a => !a.IsMandatory).MinBy(a => a.VisitMinutes)!;
            selected.Remove(drop);
            skipped.Add(drop);
            plan = Build(day, selected, cached);
        }

        // Po usunięciu kilku atrakcji (nowa kolejność, przesunięty posiłek) któraś z pominiętych może
        // jednak się zmieścić – sprawdzamy od najkrótszych.
        if (plan.OverrunMinutes <= overrunToleranceMinutes)
        {
            foreach (var attraction in skipped.OrderBy(a => a.VisitMinutes).ToList())
            {
                var candidate = Build(day, [.. selected, attraction], cached);
                if (candidate.OverrunMinutes <= overrunToleranceMinutes)
                {
                    selected.Add(attraction);
                    skipped.Remove(attraction);
                    plan = candidate;
                }
            }
        }

        var warnings = plan.Warnings.Concat(skipped.Select(a => new PlanWarning(
            PlanWarningCode.OptionalSkipped, day.Date,
            $"„{a.Name}” (opcjonalna) nie mieści się w godzinach dnia.", a.PlaceId))).ToList();
        return plan with { Skipped = skipped, Warnings = warnings };
    }

    // Najlepsza kolejność + godziny, bez usuwania atrakcji (przydział do dni sprawdza nim, czy atrakcja się mieści).
    public static DayPlan Arrange(DaySettings day, IReadOnlyList<PlanAttraction> attractions, ITravelTimes travel)
        => Build(day, attractions, CachedTravelTimes.Wrap(travel));

    private static DayPlan Build(DaySettings day, IReadOnlyList<PlanAttraction> attractions, ITravelTimes travel)
        => DayScheduler.Schedule(day, RouteOptimizer.Order(day.Start, day.End, attractions, travel), travel);

    // Dane wejściowe sprawdza endpoint (400); tu – ochrona przed błędem w kodzie wywołującym.
    private static void Validate(DaySettings day, IReadOnlyList<PlanAttraction> attractions, int overrunToleranceMinutes)
    {
        if (day.ActiveTo <= day.ActiveFrom)
            throw new ArgumentException("Koniec dnia musi być po jego początku.", nameof(day));
        if (day.Meal is { } meal && (meal.Minutes <= 0 || meal.LatestStart < meal.EarliestStart))
            throw new ArgumentException("Przerwa na posiłek: czas > 0 i okno od najwcześniejszego do najpóźniejszego startu.", nameof(day));
        if (attractions.Any(a => a.VisitMinutes < 0))
            throw new ArgumentException("Czas wizyty nie może być ujemny.", nameof(attractions));
        if (attractions.Select(a => a.PlaceId).Distinct().Count() != attractions.Count)
            throw new ArgumentException("Atrakcja występuje w dniu więcej niż raz.", nameof(attractions));
        if (overrunToleranceMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(overrunToleranceMinutes), "Tolerancja nie może być ujemna.");
    }

}
