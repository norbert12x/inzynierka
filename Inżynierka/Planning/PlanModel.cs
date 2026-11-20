using System.Text.Json.Serialization;

namespace Inżynierka.Planning;

// --- Wejście planera ---

// Atrakcja wybrana do planu: dane z katalogu + wybór użytkownika. VisitMinutes – czas wizyty
// (z katalogu albo zmieniony przez użytkownika); OpeningHours – tekst OSM do ostrzeżeń (4.4), null = nieznane.
// HasEntrance – obiekt z wejściem (muzeum, rozrywka…, EntranceCategories); tylko dla takich brak godzin
// daje ostrzeżenie – pomnik czy park bez godzin to nic dziwnego. IsAddedByPlanner – dobrana przez planer
// przy dopełnianiu pierwszego planu (TripFiller), nie przez użytkownika; użytkownik może ją usunąć.
public sealed record PlanAttraction(
    Guid PlaceId,
    string Name,
    GeoPoint Location,
    int VisitMinutes,
    bool IsMandatory,
    string? OpeningHours = null,
    bool HasEntrance = true,
    bool IsAddedByPlanner = false)
{
    public PlanPoint Point => new(Location, PlaceId);
}

// Przerwa na posiłek: czas trwania i okno, w którym może się zacząć. Planer wstawia ją między
// atrakcjami – nie przerywa zwiedzania. Stała godzina: EarliestStart = LatestStart.
public sealed record MealBreakSettings(int Minutes, TimeOnly EarliestStart, TimeOnly LatestStart)
{
    // Gdy użytkownik nie poda własnych godzin: 60 min, start między 13:30 a 15:00.
    public static MealBreakSettings Default { get; } = new(60, new TimeOnly(13, 30), new TimeOnly(15, 0));
}

// Ustawienia dnia (czas lokalny Europe/Warsaw). Start – nocleg z poprzedniej nocy (pierwszy dzień: nocleg
// tego dnia), End – nocleg tej nocy. ActiveTo – godzina powrotu do noclegu. Meal = null – bez przerwy.
public sealed record DaySettings(
    DateOnly Date,
    PlanPoint Start,
    PlanPoint End,
    TimeOnly ActiveFrom,
    TimeOnly ActiveTo,
    MealBreakSettings? Meal);

// Dzień wyjazdu wielodniowego. Lodging – nocleg tej nocy (koniec dnia); dzień startuje z noclegu
// poprzedniego dnia (pierwszy dzień – z własnego noclegu). TripPlanner zamienia go na DaySettings.
public sealed record TripDay(
    DateOnly Date,
    PlanPoint Lodging,
    TimeOnly ActiveFrom,
    TimeOnly ActiveTo,
    MealBreakSettings? Meal);

// --- Wynik planera ---

// Wizyta w planie: dojazd z poprzedniego punktu (nocleg, atrakcja), przyjazd i wyjazd. WaitMinutes – czekanie
// na otwarcie po przyjeździe (zwiedzanie od Arrival + WaitMinutes, wyjazd po VisitMinutes).
public sealed record PlannedVisit(
    PlanAttraction Attraction,
    TravelLeg TravelBefore,
    TimeOnly Arrival,
    TimeOnly Departure,
    int WaitMinutes = 0);

// Przerwa na posiłek po AfterVisits wizytach (0 = przed pierwszą, w punkcie startu) – w miejscu ostatniej wizyty.
public sealed record PlannedMeal(TimeOnly Start, TimeOnly End, int AfterVisits);

// Plan jednego dnia. Skipped – opcjonalne, które się nie zmieściły; OverrunMinutes – o ile powrót
// wypada po ActiveTo (0 = mieści się). Godziny po północy zawijają się (TimeOnly) – wtedy liczy się OverrunMinutes.
public sealed record DayPlan(
    DaySettings Settings,
    IReadOnlyList<PlannedVisit> Visits,
    PlannedMeal? Meal,
    TravelLeg ReturnLeg,
    TimeOnly ReturnArrival,
    int OverrunMinutes,
    IReadOnlyList<PlanAttraction> Skipped,
    IReadOnlyList<PlanWarning> Warnings)
{
    public int TravelMinutes => Visits.Sum(v => v.TravelBefore.Minutes) + ReturnLeg.Minutes;
}

// W JSON jako "day_overrun", "optional_skipped", "opening_hours_unknown", "opening_hours_unreadable", "maybe_closed".
[JsonConverter(typeof(JsonStringEnumConverter<PlanWarningCode>))]
public enum PlanWarningCode
{
    // Powrót do noclegu po końcu dnia (w tolerancji albo przez obowiązkowe atrakcje).
    [JsonStringEnumMemberName("day_overrun")]
    DayOverrun,
    // Opcjonalna atrakcja nie zmieściła się w dniu / wyjeździe.
    [JsonStringEnumMemberName("optional_skipped")]
    OptionalSkipped,
    // Atrakcja bez godzin otwarcia w katalogu.
    [JsonStringEnumMemberName("opening_hours_unknown")]
    OpeningHoursUnknown,
    // Zapis godzin, którego planer nie umie odczytać (np. konkretne daty) – do sprawdzenia.
    [JsonStringEnumMemberName("opening_hours_unreadable")]
    OpeningHoursUnreadable,
    // Wizyta wypada poza odczytanymi godzinami otwarcia albo w dniu zamknięcia.
    [JsonStringEnumMemberName("maybe_closed")]
    MaybeClosed
}

// Ostrzeżenie z polskim opisem dla użytkownika; Date = null – dotyczy całego wyjazdu;
// PlaceId – gdy dotyczy jednej atrakcji.
public sealed record PlanWarning(PlanWarningCode Code, DateOnly? Date, string Message, Guid? PlaceId = null);

// --- Propozycje dodatkowych atrakcji (4.5) ---

// Atrakcja z katalogu, którą można zaproponować; CategoryCodes – do dopasowania zainteresowań.
public sealed record SuggestionCandidate(PlanAttraction Attraction, IReadOnlyList<string> CategoryCodes);

// Propozycja do akceptacji – nie jest częścią planu. InsertAt – pozycja w dniu (0 = przed pierwszą wizytą),
// pod którą atrakcja mieści się bez zmiany kolejności pozostałych; godziny i wolny czas po jej dodaniu.
// Każda propozycja liczona osobno – po akceptacji kilku plan trzeba przeliczyć (może pojawić się ostrzeżenie).
public sealed record PlanSuggestion(
    DateOnly Date,
    PlanAttraction Attraction,
    int InsertAt,
    TimeOnly Arrival,
    TimeOnly Departure,
    int ExtraTravelMinutes,
    int FreeMinutesLeft,
    IReadOnlyList<string> MatchedCategories,
    IReadOnlyList<PlanWarning> Warnings);

// Plan wyjazdu: dni (każdy z własnymi ostrzeżeniami, np. przekroczenie godzin) oraz opcjonalne atrakcje,
// które nie zmieściły się w żadnym dniu (Skipped + ostrzeżenia wyjazdu w Warnings).
public sealed record TripPlan(
    IReadOnlyList<DayPlan> Days,
    IReadOnlyList<PlanAttraction> Skipped,
    IReadOnlyList<PlanWarning> Warnings);
