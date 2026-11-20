using System.ComponentModel.DataAnnotations;
using Inżynierka.Planning;

namespace Inżynierka.Models;

// --- POST /api/plans/generate – żądanie ---

// Plan bez zapisu (gość i zalogowany). Lodging – nocleg wszystkich nocy; dzień może mieć własny (zmiana hotelu).
// Typy nullowalne + [Required]: brak pola to 400, a nie wartość domyślna (np. punkt 0, 0).
public sealed class PlanGenerateRequest : IValidatableObject
{
    public const int MaxDays = 14;
    public const int MaxAttractions = 60;

    [Required(ErrorMessage = "Podaj nocleg (punkt startu i końca dni).")]
    public PlanLocationRequest? Lodging { get; set; }

    [Required(ErrorMessage = "Podaj dni wyjazdu.")]
    [MinLength(1, ErrorMessage = "Podaj co najmniej jeden dzień.")]
    [MaxLength(MaxDays, ErrorMessage = "Wyjazd może mieć najwyżej 14 dni.")]
    public List<PlanDayRequest>? Days { get; set; }

    [Required(ErrorMessage = "Wybierz atrakcje do planu.")]
    [MinLength(1, ErrorMessage = "Wybierz co najmniej jedną atrakcję.")]
    [MaxLength(MaxAttractions, ErrorMessage = "Plan może mieć najwyżej 60 atrakcji.")]
    public List<PlanAttractionRequest>? Attractions { get; set; }

    // Kody kategorii (GET /api/attraction-categories) – do propozycji dodatkowych atrakcji; pusta lista – każda kategoria.
    [MaxLength(20, ErrorMessage = "Najwyżej 20 zainteresowań.")]
    public List<string>? Interests { get; set; }

    // Pierwszy plan: planer dopełnia każdy dzień atrakcjami z katalogu (oznaczone addedByPlanner).
    // false – tylko wybrane atrakcje.
    public bool FillDays { get; set; } = true;

    // false – bez propozycji (szybciej, np. przy przeliczaniu).
    public bool IncludeSuggestions { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Days is { Count: > 0 } days && days.All(d => d.Date.HasValue))
        {
            for (var i = 1; i < days.Count; i++)
                if (days[i].Date <= days[i - 1].Date)
                {
                    yield return new ValidationResult("Dni muszą mieć różne daty w kolejności rosnącej.", [nameof(Days)]);
                    break;
                }
        }

        if (Attractions is { Count: > 0 } attractions)
        {
            var duplicates = attractions.Where(a => a.Id.HasValue).GroupBy(a => a.Id).Where(g => g.Count() > 1).ToList();
            if (duplicates.Count > 0)
                yield return new ValidationResult(
                    $"Atrakcja występuje w planie więcej niż raz: {string.Join(", ", duplicates.Select(g => g.Key))}.",
                    [nameof(Attractions)]);
        }

        if (Interests is { } interests && interests.Any(string.IsNullOrWhiteSpace))
            yield return new ValidationResult("Puste kody zainteresowań.", [nameof(Interests)]);
    }
}

public sealed class PlanLocationRequest
{
    [Required(ErrorMessage = "Podaj szerokość geograficzną.")]
    [Range(-90, 90, ErrorMessage = "Szerokość od -90 do 90.")]
    public double? Latitude { get; set; }

    [Required(ErrorMessage = "Podaj długość geograficzną.")]
    [Range(-180, 180, ErrorMessage = "Długość od -180 do 180.")]
    public double? Longitude { get; set; }

    // Etykieta dla użytkownika (np. nazwa hotelu) – nie trafia do żadnej usługi zewnętrznej.
    [StringLength(200)]
    public string? Name { get; set; }
}

public sealed class PlanDayRequest
{
    [Required(ErrorMessage = "Podaj datę dnia.")]
    public DateOnly? Date { get; set; }

    // Godziny aktywności (czas lokalny); brak – Planner:DefaultDayStart / DefaultDayEnd. End = powrót do noclegu.
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }

    // Nocleg tej nocy, gdy inny niż Lodging wyjazdu; następny dzień startuje stąd.
    public PlanLocationRequest? Lodging { get; set; }

    // Brak – domyślna przerwa (Planner:Meal*); Enabled=false – dzień bez przerwy.
    public PlanMealRequest? Meal { get; set; }
}

public sealed class PlanMealRequest
{
    public bool Enabled { get; set; } = true;

    [Range(10, 240, ErrorMessage = "Przerwa od 10 do 240 min.")]
    public int? Minutes { get; set; }

    // Okno startu przerwy; stała godzina – obie takie same.
    public TimeOnly? EarliestStart { get; set; }
    public TimeOnly? LatestStart { get; set; }
}

public sealed class PlanAttractionRequest
{
    [Required(ErrorMessage = "Podaj id atrakcji.")]
    public Guid? Id { get; set; }

    // false – opcjonalna (może wypaść, gdy dzień się nie mieści).
    public bool Mandatory { get; set; } = true;

    // Własny czas wizyty; brak – czas z katalogu.
    [Range(5, 600, ErrorMessage = "Czas wizyty od 5 do 600 min.")]
    public int? VisitMinutes { get; set; }
}

// --- Odpowiedź ---

public sealed record PlanGenerateResponse(
    IReadOnlyList<PlanDayResponse> Days,
    // Opcjonalne atrakcje, które nie zmieściły się w żadnym dniu.
    IReadOnlyList<PlanAttractionResponse> Skipped,
    // Ostrzeżenia całego wyjazdu (date = null); ostrzeżenia dni są w Days[].Warnings.
    IReadOnlyList<PlanWarning> Warnings,
    // Propozycje do akceptacji – nie są częścią planu.
    IReadOnlyList<PlanSuggestionResponse> Suggestions,
    int TravelMinutes,
    // Podpis czasów z macierzy OpenRouteService (CC-BY-SA 4.0); null – same szacunki.
    string? TravelTimesAttribution);

public sealed record PlanDayResponse(
    DateOnly Date,
    PlanLocationResponse Start,
    PlanLocationResponse End,
    TimeOnly ActiveFrom,
    TimeOnly ActiveTo,
    IReadOnlyList<PlanVisitResponse> Visits,
    PlannedMeal? Meal,
    PlanLegResponse ReturnLeg,
    TimeOnly ReturnArrival,
    int OverrunMinutes,
    int TravelMinutes,
    IReadOnlyList<PlanWarning> Warnings,
    // Nawigacja całego dnia samochodem; kilka linków, gdy wizyt jest więcej niż 9.
    IReadOnlyList<string> GoogleMapsUrls);

public sealed record PlanLocationResponse(double Latitude, double Longitude, string? Name);

public sealed record PlanVisitResponse(
    Guid PlaceId,
    string Name,
    double Latitude,
    double Longitude,
    bool Mandatory,
    // Dobrana przez planer przy dopełnianiu dnia (nie wybrana przez użytkownika) – można ją usunąć.
    bool AddedByPlanner,
    int VisitMinutes,
    VisitMinutesSource VisitMinutesSource,
    // Dojazd z poprzedniego punktu (nocleg albo poprzednia wizyta).
    PlanLegResponse TravelBefore,
    TimeOnly Arrival,
    // Czekanie na otwarcie po przyjeździe (zwiedzanie od arrival + waitMinutes); 0 – bez czekania.
    int WaitMinutes,
    TimeOnly Departure,
    string? OpeningHours);

// IsApproximate – czas z szacunku („czas przybliżony”), nie z macierzy ORS.
public sealed record PlanLegResponse(
    TravelMode Mode,
    int Minutes,
    int DistanceMeters,
    TravelTimeSource Source,
    bool IsApproximate,
    string GoogleMapsUrl,
    string GoogleMapsTransitUrl);

public sealed record PlanAttractionResponse(Guid PlaceId, string Name, bool Mandatory, int VisitMinutes);

public sealed record PlanSuggestionResponse(
    DateOnly Date,
    Guid PlaceId,
    string Name,
    double Latitude,
    double Longitude,
    int VisitMinutes,
    IReadOnlyList<string> Categories,
    // Pozycja w dniu (0 = przed pierwszą wizytą), godziny i koszt – liczone dla tej jednej propozycji.
    int InsertAt,
    TimeOnly Arrival,
    TimeOnly Departure,
    int ExtraTravelMinutes,
    int FreeMinutesLeft,
    IReadOnlyList<string> MatchedInterests,
    IReadOnlyList<PlanWarning> Warnings);
