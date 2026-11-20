using Inżynierka.Planning;

namespace Inżynierka.Configuration;

// Sekcja "Planner" w appsettings.json: wartości domyślne i progi planera (E4). Wartości startowe
// takie jak stałe w Planning/ – testy planera nie zależą od konfiguracji.
public sealed class PlannerOptions
{
    public const string SectionName = "Planner";

    // Godziny dnia, gdy użytkownik ich nie poda (koniec = powrót do noclegu).
    public TimeOnly DefaultDayStart { get; set; } = new(9, 0);
    public TimeOnly DefaultDayEnd { get; set; } = new(18, 0);

    // Domyślna przerwa na posiłek: czas i okno startu.
    public int MealMinutes { get; set; } = MealBreakSettings.Default.Minutes;
    public TimeOnly MealEarliestStart { get; set; } = MealBreakSettings.Default.EarliestStart;
    public TimeOnly MealLatestStart { get; set; } = MealBreakSettings.Default.LatestStart;

    // Przekroczenie dnia do tylu minut – tylko ostrzeżenie; większe – usuwanie najkrótszych opcjonalnych.
    public int OverrunToleranceMinutes { get; set; } = DayPlanner.DefaultOverrunToleranceMinutes;

    // Kategorie obiektów z wejściem (ostrzeżenie o braku godzin). null – EntranceCategories.DefaultCodes.
    // Tablica, nie lista: binder konfiguracji dopisywałby elementy do listy z wartościami domyślnymi.
    public string[]? EntranceCategories { get; set; }

    // Atrakcje z katalogu do dopełnienia dnia i propozycji: najwyżej tyle km od któregoś noclegu (linia prosta).
    public double CandidateMaxDistanceKm { get; set; } = 25;

    // Dopełnianie pierwszego planu: „mała” atrakcja (wizyta do tylu minut) trafia do planu tylko po drodze,
    // gdy dokłada najwyżej OnTheWayMaxDetourMinutes jazdy.
    public int SmallAttractionMaxMinutes { get; set; } = new FillSettings().SmallMaxVisitMinutes;
    public int OnTheWayMaxDetourMinutes { get; set; } = new FillSettings().OnTheWayMaxDetourMinutes;

    // Propozycje dodatkowych atrakcji (do akceptacji): ile na dzień.
    public int MaxSuggestionsPerDay { get; set; } = SuggestionFinder.DefaultMaxPerDay;

    public MealBreakSettings DefaultMeal => new(MealMinutes, MealEarliestStart, MealLatestStart);

    public FillSettings Fill => new(SmallAttractionMaxMinutes, OnTheWayMaxDetourMinutes);

    public IReadOnlySet<string> EntranceCategoryCodes
        => EntranceCategories is { Length: > 0 } codes ? codes.ToHashSet() : Planning.EntranceCategories.DefaultCodes;
}
