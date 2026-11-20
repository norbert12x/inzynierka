using Inżynierka.Planning;

namespace Inżynierka.Tests;

// Wymyślone dane dla testów planera: punkt (0, x), czas przejazdu = |Δszerokość| + |Δdługość| minut.
// Dzięki temu oczekiwane godziny da się policzyć w pamięci.
internal static class PlanningTestData
{
    public static readonly DateOnly Date = new(2027, 5, 15);

    public static PlanPoint Lodging(double x) => new(new GeoPoint(0, x));

    // Domyślnie otwarte zawsze – testy kolejności i godzin nie dostają ostrzeżeń o godzinach otwarcia.
    public static PlanAttraction Attraction(string name, double x, int visitMinutes, bool mandatory = true, double y = 0,
        string? hours = "24/7", bool hasEntrance = true)
        => new(Guid.NewGuid(), name, new GeoPoint(y, x), visitMinutes, mandatory, hours, hasEntrance);

    public static DaySettings Day(string from = "09:00", string to = "18:00", MealBreakSettings? meal = null,
        double startX = 0, double endX = 0)
        => new(Date, Lodging(startX), Lodging(endX), TimeOnly.Parse(from), TimeOnly.Parse(to), meal);

    public static MealBreakSettings Meal(int minutes = 60, string earliest = "12:00", string latest = "14:00")
        => new(minutes, TimeOnly.Parse(earliest), TimeOnly.Parse(latest));

    public static TimeOnly At(string time) => TimeOnly.Parse(time);
}

internal sealed class FakeTravelTimes : ITravelTimes
{
    public TravelLeg GetLeg(PlanPoint from, PlanPoint to)
    {
        var minutes = Math.Abs(from.Location.Latitude - to.Location.Latitude)
                    + Math.Abs(from.Location.Longitude - to.Location.Longitude);
        return new TravelLeg(TravelMode.Driving, (int)Math.Round(minutes), 0, TravelTimeSource.OpenRouteService);
    }
}
