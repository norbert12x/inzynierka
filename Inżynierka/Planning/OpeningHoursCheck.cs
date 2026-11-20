namespace Inżynierka.Planning;

// Ostrzeżenie o godzinach otwarcia dla zaplanowanej wizyty (MVP: tylko ostrzeżenie, bez zmiany kolejności).
// Brak godzin → „nieznane” (tylko obiekty z wejściem), nieczytelny zapis → „do sprawdzenia”,
// wizyta poza godzinami → „może być zamknięte”. Wpisane godziny sprawdzamy u każdej atrakcji.
public static class OpeningHoursCheck
{
    private static readonly string[] DayNames = ["niedziela", "poniedziałek", "wtorek", "środa", "czwartek", "piątek", "sobota"];

    // Najdłuższe czekanie na otwarcie; dłużej – nie czekamy, plan dostaje ostrzeżenie „może być zamknięte”.
    public const int MaxWaitMinutes = 60;

    // Ile czekać na otwarcie przy przyjeździe o arrival (minuty od północy): 0, gdy otwarte, godziny nieznane
    // albo nieczytelne, gdy tego dnia już nie otworzą albo czekanie byłoby dłuższe niż MaxWaitMinutes.
    // Zmienia tylko godziny wizyty, nie kolejność.
    public static int WaitMinutes(PlanAttraction attraction, DateOnly date, int arrival)
    {
        if (string.IsNullOrWhiteSpace(attraction.OpeningHours) || OpeningHours.TryParse(attraction.OpeningHours) is not { } hours)
            return 0;

        var intervals = hours.IntervalsOn(date);
        if (intervals.Any(i => i.FromMinute <= arrival && arrival < i.ToMinute))
            return 0;
        var opening = intervals.Where(i => i.FromMinute > arrival).Select(i => (int?)i.FromMinute).Min();
        return opening is { } from && from - arrival <= MaxWaitMinutes ? from - arrival : 0;
    }

    // arrival – początek zwiedzania (po ewentualnym czekaniu), departure – wyjazd; minuty od północy dnia wizyty
    // (mogą przekraczać 1440 przy przekroczeniu dnia).
    public static PlanWarning? Check(PlanAttraction attraction, DateOnly date, int arrival, int departure)
    {
        if (string.IsNullOrWhiteSpace(attraction.OpeningHours))
            return attraction.HasEntrance
                ? new PlanWarning(PlanWarningCode.OpeningHoursUnknown, date,
                    $"Brak godzin otwarcia „{attraction.Name}” – sprawdź je przed wyjazdem.", attraction.PlaceId)
                : null;

        var hours = OpeningHours.TryParse(attraction.OpeningHours);
        if (hours is null)
            return new PlanWarning(PlanWarningCode.OpeningHoursUnreadable, date,
                $"Nie udało się odczytać godzin otwarcia „{attraction.Name}” ({attraction.OpeningHours}) – sprawdź je przed wyjazdem.",
                attraction.PlaceId);

        var intervals = hours.IntervalsOn(date);
        if (intervals.Any(i => i.FromMinute <= arrival && departure <= i.ToMinute))
            return null;

        var visit = $"{OpenInterval.Format(arrival % 1440)}–{OpenInterval.Format(departure % 1440)}";
        var message = intervals.Count == 0
            ? $"„{attraction.Name}” może być zamknięta w tym dniu ({DayNames[(int)date.DayOfWeek]}, godziny: {attraction.OpeningHours})."
            : $"Wizyta w „{attraction.Name}” ({visit}) wypada poza godzinami otwarcia ({string.Join(", ", intervals)}).";
        return new PlanWarning(PlanWarningCode.MaybeClosed, date, message, attraction.PlaceId);
    }
}
