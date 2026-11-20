namespace Inżynierka.Planning;

// Godziny dnia dla zadanej kolejności atrakcji: przejazdy, czekanie na otwarcie, wizyty, posiłek, powrót do noclegu.
// Nie zmienia kolejności ani nie usuwa atrakcji – dlatego służy też do przeliczenia planu po ręcznej
// zmianie kolejności, czasu wizyty albo miejsca posiłku (E5.3). Przekroczenie godzin dnia → ostrzeżenie.
public static class DayScheduler
{
    // mealAfterVisits – posiłek wstawiony przez użytkownika po tylu wizytach (0 = przed pierwszą);
    // null – według okna z day.Meal.
    public static DayPlan Schedule(DaySettings day, IReadOnlyList<PlanAttraction> ordered, ITravelTimes travel,
        int? mealAfterVisits = null)
    {
        if (mealAfterVisits is { } after && (day.Meal is null || after < 0 || after > ordered.Count))
            throw new ArgumentOutOfRangeException(nameof(mealAfterVisits),
                "Posiłek można wstawić tylko w dniu z przerwą, między 0 a liczbą wizyt.");

        var visits = new List<PlannedVisit>(ordered.Count);
        var hoursWarnings = new List<PlanWarning>();
        PlannedMeal? meal = null;
        // Dzień zaczynający się po oknie posiłku nie potrzebuje przerwy (chyba że użytkownik ją wstawił).
        var mealPending = day.Meal is { } m
            && (mealAfterVisits is not null || ToMinutes(day.ActiveFrom) <= ToMinutes(m.LatestStart));
        var time = ToMinutes(day.ActiveFrom);
        var current = day.Start;
        TravelLeg returnLeg = null!;

        for (var i = 0; i <= ordered.Count; i++)
        {
            var isReturn = i == ordered.Count;
            var next = isReturn ? day.End : ordered[i].Point;
            var leg = travel.GetLeg(current, next);

            int? mealStart = null;
            if (mealPending)
                mealStart = mealAfterVisits is { } chosen
                    ? (chosen == visits.Count ? time : (int?)null)
                    : MealStart(day.Meal!, time, isReturn ? null
                        : leg.Minutes + OpeningHoursCheck.WaitMinutes(ordered[i], day.Date, time + leg.Minutes) + ordered[i].VisitMinutes);
            if (mealStart is not null)
            {
                time = mealStart.Value + day.Meal!.Minutes;
                meal = new PlannedMeal(ToTime(mealStart.Value), ToTime(time), visits.Count);
                mealPending = false;
            }

            time += leg.Minutes;
            if (isReturn)
            {
                returnLeg = leg;
                break;
            }

            // Przyjazd przed otwarciem – czekamy (najwyżej OpeningHoursCheck.MaxWaitMinutes), kolejność bez zmian.
            var arrival = time;
            var wait = OpeningHoursCheck.WaitMinutes(ordered[i], day.Date, arrival);
            time += wait + ordered[i].VisitMinutes;
            visits.Add(new PlannedVisit(ordered[i], leg, ToTime(arrival), ToTime(time), wait));
            if (OpeningHoursCheck.Check(ordered[i], day.Date, arrival + wait, time) is { } hoursWarning)
                hoursWarnings.Add(hoursWarning);
            current = next;
        }

        var overrun = Math.Max(0, time - ToMinutes(day.ActiveTo));
        var warnings = new List<PlanWarning>();
        if (overrun > 0)
            warnings.Add(new PlanWarning(PlanWarningCode.DayOverrun, day.Date,
                $"Powrót do noclegu o {ToTime(time):HH:mm} – {overrun} min po końcu dnia ({day.ActiveTo:HH:mm}). "
                + "Możesz usunąć wybraną atrakcję, przenieść ją na inny dzień albo zostawić plan bez zmian."));
        warnings.AddRange(hoursWarnings);

        return new DayPlan(day, visits, meal, returnLeg, ToTime(time), overrun, [], warnings);
    }

    // Kiedy zacząć posiłek przed kolejnym odcinkiem (null – jeszcze nie teraz):
    // okno już trwa → od razu; następny przejazd z wizytą skończyłby się po najpóźniejszym starcie
    // → czekamy na początek okna. Przed powrotem do noclegu (nextMinutes = null) nie czekamy –
    // gdy okno się nie zaczęło, posiłek wypada już po zwiedzaniu.
    private static int? MealStart(MealBreakSettings meal, int time, int? nextMinutes)
    {
        var earliest = ToMinutes(meal.EarliestStart);
        if (time >= earliest)
            return time;
        if (nextMinutes is { } next && time + next > ToMinutes(meal.LatestStart))
            return earliest;
        return null;
    }

    public static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;

    // Po północy zawija się (TimeOnly) – przekroczenie dnia pokazuje OverrunMinutes i ostrzeżenie.
    public static TimeOnly ToTime(int minutes) => TimeOnly.MinValue.AddMinutes(minutes);
}
