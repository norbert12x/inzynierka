namespace Inżynierka.Planning;

// Plan wyjazdu wielodniowego: przydział atrakcji do dni, potem DayPlanner dla każdego dnia.
//
// Przydział (wstawianie z limitem dnia): atrakcja trafia do dnia z najniższym kosztem = dodatkowa jazda
// (blisko noclegu albo innych atrakcji tego dnia) + LoadWeight × czas zwiedzania już przydzielony temu dniu –
// dzięki karze za obciążenie wybrane atrakcje rozkładają się na wszystkie dni, a bliskie siebie nadal trafiają
// razem (resztę dnia dopełnia TripFiller). Tylko dni, które mieszczą atrakcję w godzinach (bez tolerancji).
// Remis – dzień mniej obciążony, potem wcześniejszy.
// Kolejność przydziału: obowiązkowe przed opcjonalnymi, w każdej grupie najpierw najdalsze od noclegów –
// one wyznaczają kierunek dnia, bliższe dołączają do nich po drodze. Atrakcja, która nie mieści się nigdzie,
// trafia do dnia z najmniejszym przekroczeniem; dalej decyduje DayPlanner (tolerancja, usuwanie najkrótszych).
// Opcjonalne usunięte z dnia próbujemy jeszcze wstawić do innego dnia, który ma na nie miejsce.
public static class TripPlanner
{
    // Ile minut „wirtualnej jazdy” kosztuje każda minuta zwiedzania już zaplanowana w dniu: dzień z 4 h
    // zwiedzania przegrywa z pustym, jeśli atrakcja dokłada do pustego mniej niż ok. 60 min jazdy.
    public const double LoadWeight = 0.25;

    public static TripPlan Plan(IReadOnlyList<TripDay> tripDays, IReadOnlyList<PlanAttraction> attractions,
        ITravelTimes travel, int overrunToleranceMinutes = DayPlanner.DefaultOverrunToleranceMinutes)
    {
        Validate(tripDays, attractions);
        var cached = CachedTravelTimes.Wrap(travel);
        var days = ToDaySettings(tripDays);
        var assigned = days.Select(_ => new List<PlanAttraction>()).ToList();
        var dayTravel = days.Select(d => cached.GetLeg(d.Start, d.End).Minutes).ToArray();

        var order = attractions
            .OrderBy(a => a.IsMandatory ? 0 : 1)
            .ThenByDescending(a => days.Min(d => Detour(d, a, cached)))
            .ToList();
        foreach (var attraction in order)
        {
            var options = days.Select((day, i) =>
            {
                var plan = DayPlanner.Arrange(day, [.. assigned[i], attraction], cached);
                var visits = assigned[i].Sum(a => a.VisitMinutes);
                return (Index: i, Plan: plan, Cost: plan.TravelMinutes - dayTravel[i] + LoadWeight * visits,
                    Load: visits + dayTravel[i]);
            }).ToList();

            var fitting = options.Where(o => o.Plan.OverrunMinutes == 0).ToList();
            var best = fitting.Count > 0
                ? fitting.OrderBy(o => o.Cost).ThenBy(o => o.Load).ThenBy(o => o.Index).First()
                : options.OrderBy(o => o.Plan.OverrunMinutes).ThenBy(o => o.Index).First();
            assigned[best.Index].Add(attraction);
            dayTravel[best.Index] = best.Plan.TravelMinutes;
        }

        var plans = days.Select((day, i) => DayPlanner.Plan(day, assigned[i], cached, overrunToleranceMinutes)).ToList();

        // Usunięte z jednego dnia – do innego, jeśli tam mieszczą się w godzinach (bez tolerancji).
        var skipped = new List<PlanAttraction>();
        foreach (var attraction in plans.SelectMany(p => p.Skipped).OrderBy(a => a.VisitMinutes).ToList())
        {
            var target = plans
                .Select((plan, i) => (Index: i, Current: plan,
                    Candidate: DayPlanner.Arrange(days[i], [.. Attractions(plan), attraction], cached)))
                .Where(o => o.Candidate.OverrunMinutes == 0)
                .OrderBy(o => o.Candidate.TravelMinutes - o.Current.TravelMinutes)
                .Select(o => (int?)o.Index)
                .FirstOrDefault();

            if (target is { } index)
                plans[index] = DayPlanner.Plan(days[index], [.. Attractions(plans[index]), attraction], cached, overrunToleranceMinutes);
            else
                skipped.Add(attraction);
        }

        // Pominięte opcjonalne raportujemy raz, dla całego wyjazdu.
        var dayPlans = plans
            .Select(p => p with { Skipped = [], Warnings = p.Warnings.Where(w => w.Code != PlanWarningCode.OptionalSkipped).ToList() })
            .ToList();
        var warnings = skipped.Select(a => new PlanWarning(PlanWarningCode.OptionalSkipped, null,
            $"„{a.Name}” (opcjonalna) nie mieści się w żadnym dniu wyjazdu.", a.PlaceId)).ToList();
        return new TripPlan(dayPlans, skipped, warnings);
    }

    // Dzień N startuje z noclegu dnia N−1, pierwszy – z własnego noclegu; kończy się w noclegu tej nocy.
    public static IReadOnlyList<DaySettings> ToDaySettings(IReadOnlyList<TripDay> days)
        => days.Select((day, i) => new DaySettings(day.Date, i == 0 ? day.Lodging : days[i - 1].Lodging,
            day.Lodging, day.ActiveFrom, day.ActiveTo, day.Meal)).ToList();

    // Ile jazdy dokłada atrakcja do pustego dnia (start → atrakcja → koniec zamiast start → koniec).
    private static int Detour(DaySettings day, PlanAttraction attraction, ITravelTimes travel)
        => travel.GetLeg(day.Start, attraction.Point).Minutes + travel.GetLeg(attraction.Point, day.End).Minutes
         - travel.GetLeg(day.Start, day.End).Minutes;

    private static List<PlanAttraction> Attractions(DayPlan plan) => plan.Visits.Select(v => v.Attraction).ToList();

    // Dane wejściowe sprawdza endpoint (400); tu – ochrona przed błędem w kodzie wywołującym.
    // Pojedyncze dni sprawdza DayPlanner.
    private static void Validate(IReadOnlyList<TripDay> days, IReadOnlyList<PlanAttraction> attractions)
    {
        if (days.Count == 0)
            throw new ArgumentException("Wyjazd musi mieć co najmniej jeden dzień.", nameof(days));
        for (var i = 1; i < days.Count; i++)
            if (days[i].Date <= days[i - 1].Date)
                throw new ArgumentException("Dni wyjazdu muszą mieć rosnące, różne daty.", nameof(days));
        if (attractions.Select(a => a.PlaceId).Distinct().Count() != attractions.Count)
            throw new ArgumentException("Atrakcja występuje w wyjeździe więcej niż raz.", nameof(attractions));
    }
}
