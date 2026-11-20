namespace Inżynierka.Planning;

// Wynik wstawienia atrakcji do dnia: pozycja (0 = przed pierwszą wizytą), nowy plan dnia, dodatkowa jazda
// i ostrzeżenia samej wstawionej atrakcji.
internal sealed record InsertionResult(int Position, DayPlan Plan, int ExtraTravelMinutes, IReadOnlyList<PlanWarning> OwnWarnings)
{
    public bool HasUnknownHours => OwnWarnings.Any(w => w.Code == PlanWarningCode.OpeningHoursUnknown);
}

// Wstawienie atrakcji w najtańsze miejsce dnia bez zmiany kolejności pozostałych wizyt (propozycje, dopełnianie).
internal static class Insertion
{
    // null – nigdzie się nie mieści: przekroczenie dnia, atrakcja zamknięta / z nieczytelnymi godzinami
    // albo przesunęłaby inną wizytę poza godziny otwarcia. Remis dodatkowej jazdy – wcześniejsza pozycja.
    public static InsertionResult? Best(DayPlan day, PlanAttraction candidate, ITravelTimes travel)
    {
        if (day.OverrunMinutes > 0)
            return null;

        var visits = day.Visits.Select(v => v.Attraction).ToList();
        var closedBefore = ClosedCount(day, except: null);
        InsertionResult? best = null;
        for (var position = 0; position <= visits.Count; position++)
        {
            var ordered = visits.ToList();
            ordered.Insert(position, candidate);
            var result = DayScheduler.Schedule(day.Settings, ordered, travel);
            if (result.OverrunMinutes > 0)
                continue;

            var own = result.Warnings.Where(w => w.PlaceId == candidate.PlaceId).ToList();
            if (own.Any(w => w.Code is PlanWarningCode.MaybeClosed or PlanWarningCode.OpeningHoursUnreadable)
                || ClosedCount(result, except: candidate.PlaceId) > closedBefore)
                continue;

            // Może wyjść ujemnie (np. odcinek pieszo zamiast autem z parkowaniem) – liczymy 0.
            var extra = Math.Max(0, result.TravelMinutes - day.TravelMinutes);
            if (best is null || extra < best.ExtraTravelMinutes)
                best = new InsertionResult(position, result, extra, own);
        }
        return best;
    }

    public static int ClosedCount(DayPlan day, Guid? except)
        => day.Warnings.Count(w => w.Code == PlanWarningCode.MaybeClosed && w.PlaceId != except);
}
